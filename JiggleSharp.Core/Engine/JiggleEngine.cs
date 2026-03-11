using JiggleSharp.Core.Input;
using JiggleSharp.Core.Idle;
using Serilog;

namespace JiggleSharp.Core.Engine;

/// <summary>
/// Core engine that monitors user idle time and periodically performs a
/// natural-looking mouse movement to prevent the system from locking or
/// marking the user as away.
///
/// How it works:
///   1. The engine subscribes to <see cref="IIdleTimeProvider.IdleTimeChanged"/>
///      at construction time.
///   2. Each time the provider reports an updated idle duration, the engine
///      checks whether <see cref="JiggleOptions.IdleTimeout"/> has elapsed
///      since both the last external activity and its own last jiggle.
///   3. When both conditions are met the engine generates a WindMouse path —
///      a physics-based cursor trajectory with configurable gravity, wind, and
///      speed — and dispatches it to <see cref="IInputInjector"/> step by step.
///
/// WindMouse algorithm:
///   Applies a simple gravity + wind force model to accumulate velocity
///   toward a random target offset, producing curved, human-like movement
///   rather than straight-line jumps. All tuning parameters are driven by
///   <see cref="Configuration"/> so behaviour can be adjusted at runtime
///   without rebuilding.
/// </summary>
public sealed class JiggleEngine
{
    // =========================================================================
    // Dependencies
    // =========================================================================
    private readonly IInputInjector    _inputInjector;
    private readonly IIdleTimeProvider _idleTimeProvider;

    // =========================================================================
    // Configuration & runtime state
    // =========================================================================

    /// <summary>
    /// Live configuration. Can be replaced at runtime; the next idle event
    /// will pick up any changes automatically.
    /// </summary>
    public JiggleOptions Configuration { get; set; }

    /// <summary>UTC timestamp of the most recent jiggle the engine performed.</summary>
    private DateTime _lastAction = DateTime.Now;

    /// <summary>
    /// Most recent idle duration reported by the provider. Updated on every
    /// <see cref="IIdleTimeProvider.IdleTimeChanged"/> event.
    /// </summary>
    private TimeSpan _timeSinceLastMovement = TimeSpan.Zero;

    /// <summary>
    /// When <c>true</c> (the current default), each path point is delayed by
    /// its per-point microsecond value for smoother, more human-like playback.
    /// When <c>false</c>, a fixed 5 ms inter-point delay is used instead.
    /// </summary>
    private readonly bool _smoothMode = true;

    /// <summary>Guards the event loop / path execution against a stopped engine.</summary>
    private volatile bool _running = true;
    
    /// <summary>Shared RNG instance; not thread-safe but only accessed on the event callback thread.</summary>
    private readonly Random _rng = new();
    
    public async Task Stop()
    {
        _running = false;
        await _idleTimeProvider.StopAsync();
    }

    public void Start()
    {
        _running = true;
        _idleTimeProvider.Start();
    }
    
    public bool IsRunning => _running;
    
    // =========================================================================
    // Construction
    // =========================================================================

    public JiggleEngine(
        JiggleOptions      config,
        IIdleTimeProvider  idleTimeProvider,
        IInputInjector     inputInjector)
    {
        _inputInjector    = inputInjector;
        _idleTimeProvider = idleTimeProvider;
        Configuration     = config;

        _idleTimeProvider.IdleTimeChanged += IdleTimeProviderOnIdleTimeChanged;
    }

    // =========================================================================
    // Idle event handler
    // =========================================================================

    /// <summary>
    /// Called by the idle provider whenever the idle duration changes.
    /// Triggers a jiggle when both:
    ///   - The reported idle time exceeds <see cref="JiggleOptions.IdleTimeout"/>, and
    ///   - The engine has not itself acted within the same timeout window
    ///     (prevents re-triggering immediately after our own mouse movement
    ///     resets the compositor's idle clock).
    /// </summary>
    private void IdleTimeProviderOnIdleTimeChanged(object? sender, IdleTimeChangedEventArgs e)
    {
        if (!_running) return;
        
        _timeSinceLastMovement = e.IdleTime;

        bool idleLongEnough      = _timeSinceLastMovement > Configuration.IdleTimeout;
        bool notActedRecently    = DateTime.Now.Subtract(_lastAction) > Configuration.IdleTimeout;

        if (idleLongEnough && notActedRecently)
        {
            _timeSinceLastMovement = TimeSpan.Zero;
            _lastAction            = DateTime.Now;
            PerformWindMove();
        }
    }

    // =========================================================================
    // WindMouse path generator
    // =========================================================================

    /// <summary>
    /// Generates a list of relative mouse movement steps from the current
    /// cursor position toward (<paramref name="targetX"/>, <paramref name="targetY"/>)
    /// using the WindMouse algorithm.
    ///
    /// The algorithm maintains a velocity vector that is pulled toward the
    /// target by a gravity force and perturbed by a stochastic wind force,
    /// producing smooth, curved trajectories. Velocity is clamped to
    /// <c>maxStep</c> each iteration to prevent overshooting.
    ///
    /// Generation stops when the cursor is within <c>targetRadius</c> of the
    /// target or the path reaches <see cref="JiggleOptions.PathPointsMaximum"/>.
    /// </summary>
    /// <param name="targetX">Horizontal offset from the current cursor position.</param>
    /// <param name="targetY">Vertical offset from the current cursor position.</param>
    private List<PathPoint> GenerateWindPath(double targetX, double targetY)
    {
        var path = new List<PathPoint>();

        // Sample all tuning parameters once per move so the trajectory is
        // consistent within a single jiggle but varies between jiggles.
        double mouseSpeed   = Randf(Configuration.MouseSpeedMinimum,        Configuration.MouseSpeedMaximum);
        double gravity      = Randf(Configuration.GravityMinimum,           Configuration.GravityMaximum);
        double wind         = Randf(Configuration.WindMinimum,              Configuration.WindMaximum);
        double targetRadius = Randf(Configuration.TargetRadiusMinimum,      Configuration.TargetRadiusMaximum);
        double maxStep      = Randf(Configuration.VelocityMaxStepMinimum,   Configuration.VelocityMaxStepMaximum);

        // Current position (relative to start) and velocity accumulator.
        double x = 0, y = 0;
        double vx = 0, vy = 0;

        // Wind force components — decay each step then add fresh randomness.
        double wx = 0, wy = 0;

        while (Hypot(targetX - x, targetY - y) > targetRadius
               && path.Count < Configuration.PathPointsMaximum)
        {
            double dist = Hypot(targetX - x, targetY - y);

            // Decay existing wind and inject a random perturbation.
            wx = wx / Math.Sqrt(3.0) + Randf(-wind, wind) / Math.Sqrt(5.0);
            wy = wy / Math.Sqrt(3.0) + Randf(-wind, wind) / Math.Sqrt(5.0);

            // Accelerate toward target (gravity) plus wind, scaled by speed.
            if (dist > 0)
            {
                vx += (wx + (targetX - x) * gravity / dist) / mouseSpeed;
                vy += (wy + (targetY - y) * gravity / dist) / mouseSpeed;
            }

            // Clamp velocity magnitude to maxStep to prevent large jumps.
            double vel = Hypot(vx, vy);
            if (vel > maxStep)
            {
                vx = vx / vel * maxStep;
                vy = vy / vel * maxStep;
            }

            // Convert continuous position delta to integer pixel steps.
            int dx = (int)Math.Round(x + vx) - (int)Math.Round(x);
            int dy = (int)Math.Round(y + vy) - (int)Math.Round(y);

            x += vx;
            y += vy;

            // Only record steps that actually move the cursor at least one pixel.
            if (dx != 0 || dy != 0)
                path.Add(new PathPoint(dx, dy, (int)Randf(Configuration.MovementDelayMinimum, Configuration.MovementDelayMaximum)));
        }

        return path;
    }

    // =========================================================================
    // Path executor
    // =========================================================================

    /// <summary>
    /// Dispatches a generated path to the input injector using the configured
    /// execution mode (<see cref="_smoothMode"/>).
    /// </summary>
    private void ExecutePath(List<PathPoint> path)
    {
        if (_smoothMode)
            ExecutePathSmooth(path);
        else
            ExecutePathBatch(path);
    }

    /// <summary>
    /// Executes each path point with a fixed 5 ms inter-point delay.
    /// Simpler than smooth mode but produces less natural-looking motion.
    /// </summary>
    private void ExecutePathBatch(List<PathPoint> path)
    {
        foreach (var point in path)
        {
            if (!_running) break;

            Task.Run(() => _inputInjector.MoveMouseAsync(point.Dx, point.Dy, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// Executes each path point with its per-point microsecond delay
    /// (converted to milliseconds, minimum 1 ms). Produces more human-like
    /// timing variation between steps.
    /// </summary>
    private void ExecutePathSmooth(List<PathPoint> path)
    {
        foreach (var point in path)
        {
            if (!_running) break;

            Task.Run(() => _inputInjector.MoveMouseAsync(point.Dx, point.Dy, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            // DelayUs is in microseconds; Thread.Sleep takes milliseconds.
            // Clamp to a minimum of 1 ms to avoid a busy-wait spin.
            Thread.Sleep(Math.Max(1, point.DelayUs / 1000));
        }
    }

    /// <summary>
    /// Picks a random target offset within ±400 px, generates a WindMouse
    /// path toward it, and executes the path.
    /// </summary>
    private void PerformWindMove()
    {
        double tx = Randf(-400, 400);
        double ty = Randf(-400, 400);

        Log.Information($"    -> Target: ({tx:F0}, {ty:F0})");

        var path = GenerateWindPath(tx, ty);
        Log.Information($"    -> Path: {path.Count} points");

        ExecutePath(path);
    }

    // =========================================================================
    // Math helpers
    // =========================================================================

    /// <summary>Returns the Euclidean distance between the origin and (x, y).</summary>
    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    /// <summary>Returns a uniformly distributed random double in [min, max).</summary>
    private double Randf(double min, double max) =>
        min + _rng.NextDouble() * (max - min);
}