using System.Diagnostics;
using JiggleSharp.Core.Input;
using JiggleSharp.Core.Hosting;
using JiggleSharp.Core.Idle;

namespace JiggleSharp.Core.Engine;

public sealed class JiggleEngine
{
    private readonly ILogger _logger;
    private readonly IInputInjector _inputInjector;
    private readonly IIdleTimeProvider _idleTimeProvider;
    
    // ========================================================================
    // CONFIGURATION
    // ========================================================================
    private const string LogFile   = "/tmp/jigglemil.log";
    private const string StateFile = "/tmp/jigglemil.state";
    private const string PidFile   = "/tmp/jigglemil.pid";

    private const long   WarningLimitMs  = 30_000;
    private TimeSpan _actionTime = TimeSpan.FromSeconds(30);
    private DateTime _lastAction = DateTime.Now;
    private const long   MinActionMs     = 120_000;
    private const long   MaxActionMs     = 180_000;
    private const int    CheckIntervalMs = 1_000;

    private const double MouseSpeedMin   = 5.0;
    private const double MouseSpeedMax   = 15.0;
    private const double GravityMin      = 5.0;
    private const double GravityMax      = 10.0;
    private const double WindMin         = 1.0;
    private const double WindMax         = 5.0;
    private const double TargetRadiusMin = 2.0;
    private const double TargetRadiusMax = 5.0;
    private const double MaxStepMin      = 5.0;
    private const double MaxStepMax      = 15.0;
    private const int    MaxPathPoints   = 1000;
    private const int    MinDelayUs      = 2_000;
    private const int    MaxDelayUs      = 3_500;

    private TimeSpan _timeSinceLastMovement = TimeSpan.Zero;

    public JiggleEngine(ILogger logger, IIdleTimeProvider idleTimeProvider, IInputInjector inputInjector)
    {
        _logger = logger;
        _inputInjector = inputInjector;
        _idleTimeProvider = idleTimeProvider;
        
        _idleTimeProvider.IdleTimeChanged += IdleTimeProviderOnIdleTimeChanged;
    }

    private void IdleTimeProviderOnIdleTimeChanged(object? sender, IdleTimeChangedEventArgs e)
    {
        _timeSinceLastMovement = e.IdleTime;

        if (_timeSinceLastMovement > _actionTime && 
                DateTime.Now.Subtract(_lastAction) > _actionTime)
        {
            _timeSinceLastMovement = TimeSpan.Zero;
            _lastAction = DateTime.Now;
            PerformWindMove();
        }
    }

    // ========================================================================
    // STATE
    // ========================================================================
    private volatile bool _running = true;
    private readonly bool          _smoothMode = true;

    private readonly Random _rng = new();
    
    // ========================================================================
    // WINDMOUSE PATH GENERATOR
    // ========================================================================
    private List<PathPoint> GenerateWindPath(double targetX, double targetY)
    {
        var path = new List<PathPoint>();

        double mouseSpeed   = Randf(MouseSpeedMin,   MouseSpeedMax);
        double gravity      = Randf(GravityMin,      GravityMax);
        double wind         = Randf(WindMin,          WindMax);
        double targetRadius = Randf(TargetRadiusMin, TargetRadiusMax);
        double maxStep      = Randf(MaxStepMin,       MaxStepMax);

        double x = 0, y = 0;
        double vx = 0, vy = 0;
        double wx = 0, wy = 0;

        while (Hypot(targetX - x, targetY - y) > targetRadius
               && path.Count < MaxPathPoints)
        {
            double dist = Hypot(targetX - x, targetY - y);

            wx = wx / Math.Sqrt(3.0) + Randf(-wind, wind) / Math.Sqrt(5.0);
            wy = wy / Math.Sqrt(3.0) + Randf(-wind, wind) / Math.Sqrt(5.0);

            if (dist > 0)
            {
                vx += (wx + (targetX - x) * gravity / dist) / mouseSpeed;
                vy += (wy + (targetY - y) * gravity / dist) / mouseSpeed;
            }

            double vel = Hypot(vx, vy);
            if (vel > maxStep)
            {
                vx = vx / vel * maxStep;
                vy = vy / vel * maxStep;
            }

            int dx = (int)Math.Round(x + vx) - (int)Math.Round(x);
            int dy = (int)Math.Round(y + vy) - (int)Math.Round(y);

            x += vx;
            y += vy;

            if (dx != 0 || dy != 0)
                path.Add(new PathPoint(dx, dy, (int)Randf(MinDelayUs, MaxDelayUs)));
        }

        return path;
    }
    
    // ========================================================================
    // PATH EXECUTOR
    // ========================================================================
    private void ExecutePath(List<PathPoint> path)
    {
        if (_smoothMode)
            ExecutePathSmooth(path);
        else
            ExecutePathBatch(path);
    }

    private void ExecutePathBatch(List<PathPoint> path)
    {
        foreach (var p in path)
        {
            if (!_running) break;
            
            Task.Run(() => _inputInjector.MoveMouseAsync(p.Dx, p.Dy, CancellationToken.None))
                .GetAwaiter()
                .GetResult();
            
            Thread.Sleep(5); // 5ms
        }
    }

    private void ExecutePathSmooth(List<PathPoint> path)
    {
        foreach (var p in path)
        {
            if (!_running) break; 
            
            Task.Run(() => _inputInjector.MoveMouseAsync(p.Dx, p.Dy, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            // Convert microseconds → milliseconds (minimum 1ms)
            Thread.Sleep(Math.Max(1, p.DelayUs / 1000));
        }
    }

    private void PerformWindMove()
    {
        double tx = Randf(-400, 400);
        double ty = Randf(-400, 400);

        _logger.Info($"    -> Target: ({tx:F0}, {ty:F0})");

        var path = GenerateWindPath(tx, ty);
        _logger.Info($"    -> Path: {path.Count} points");

        ExecutePath(path);
    }
    
    
    
    // ========================================================================
    // MATH HELPERS
    // ========================================================================
    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private double Randf(double min, double max) =>
        min + _rng.NextDouble() * (max - min);

    private long NextActionLimit() =>
        MinActionMs + (long)(_rng.NextDouble() * (MaxActionMs - MinActionMs));
}