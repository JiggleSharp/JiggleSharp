using System.Runtime.InteropServices.ComTypes;

namespace JiggleSharp.Core.Engine;

public sealed class JiggleEngine
{
    private readonly Idle.IIdleTimeProvider _idle;
    private readonly Input.IInputInjector _input;
    private readonly Input.IPathGenerator _path;
    private readonly Hosting.IStateStore _state;
    private readonly Hosting.ILogger _log;
    private readonly JiggleOptions _opt;
    private readonly Random _rng;

    private long _actionLimitMs;

    private EngineStatus _lastStatus = EngineStatus.Safe;
    private string _lastEmoji = "🟢";

    public JiggleEngine(
        Idle.IIdleTimeProvider idleTimeProvider,
        Input.IInputInjector inputInjector,
        Input.IPathGenerator pathGenerator,
        Hosting.IStateStore stateStore,
        Hosting.ILogger logger,
        JiggleOptions options,
        Random random)
    {
        _idle = idleTimeProvider ?? throw new ArgumentNullException(nameof(idleTimeProvider));
        _input = inputInjector ?? throw new ArgumentNullException(nameof(inputInjector));
        _path = pathGenerator ?? throw new ArgumentNullException(nameof(pathGenerator));
        _state = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _log = logger ?? throw new ArgumentNullException(nameof(logger));
        _opt = options ?? throw new ArgumentNullException(nameof(options));
        _rng = random ?? throw new ArgumentNullException(nameof(random));

        ResetActionLimit();
    }

    /// <summary>
    /// The currently scheduled action threshold (ms idle) at which we will jiggle.
    /// </summary>
    public long CurrentActionLimitMs => _actionLimitMs;

    /// <summary>
    /// One “tick” of the engine. Call this on a fixed interval (e.g., every 1s).
    /// Returns a snapshot you can feed to a watch UI.
    /// </summary>
    public async Task<JiggleSnapshot> TickAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        TimeSpan idleTime;
        try
        {
            idleTime = await _idle.GetIdleTimeAsync(ct);
            if (idleTime.TotalMilliseconds < 0) idleTime = TimeSpan.FromMilliseconds(0);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // If idle query fails, treat as "not idle" (safe) but report error.
            _log.Error("Idle time query failed.", ex);
            idleTime = TimeSpan.FromMilliseconds(0);
        }

        // Determine status based on thresholds
        EngineStatus status;
        string emoji;

        if (idleTime.TotalMilliseconds >= _actionLimitMs)
        {
            status = EngineStatus.Acting;
            emoji = "🟡";
        }
        else if (idleTime.TotalMilliseconds >= _opt.WarningLimitMs)
        {
            status = EngineStatus.Warning;
            emoji = "🔴";
        }
        else
        {
            status = EngineStatus.Safe;
            emoji = "🟢";
        }

        // Update state file only when it changes (reduces churn)
        if (emoji != _lastEmoji)
        {
            _state.SetEmoji(emoji);
            _lastEmoji = emoji;
        }

        // Optionally log status transitions
        if (status != _lastStatus)
        {
            if (_opt.LogStatusTransitions)
                _log.Info($"Status: {status} (idle {idleTime.Seconds}s, actionLimit {_actionLimitMs / 1000}s)");
            _lastStatus = status;
        }

        // If time to act, do a jiggle
        if (status == EngineStatus.Acting)
        {
            await PerformJiggleAsync(idleTime, ct).ConfigureAwait(false);
            ResetActionLimit();

            // After an action, we typically want to “settle” a bit (optional)
            if (_opt.PostActionCooldownMs > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(_opt.PostActionCooldownMs), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
            }

            // State returns to safe after action (matching your original behavior)
            if ("🟢" != _lastEmoji)
            {
                _state.SetEmoji("🟢");
                _lastEmoji = "🟢";
            }

            status = EngineStatus.Safe;
            emoji = "🟢";
        }

        return new JiggleSnapshot(
            TimestampUtc: DateTimeOffset.UtcNow,
            Status: status,
            Emoji: emoji,
            IdleMs: idleTime,
            WarningLimitMs: TimeSpan.FromMilliseconds(_opt.WarningLimitMs),
            ActionLimitMs: TimeSpan.FromMilliseconds(_actionLimitMs),
            Mode: _opt.Mode);
    }

    private async Task PerformJiggleAsync(TimeSpan idleMs, CancellationToken ct)
    {
        // Choose a random target point. We keep your “big box” approach.
        // You can make this configurable later (radius, box size, etc.)
        double targetX = RandF(_opt.TargetMinX, _opt.TargetMaxX);
        double targetY = RandF(_opt.TargetMinY, _opt.TargetMaxY);

        if (_opt.LogActions)
        {
            _log.Info($"ACTION: idle {idleMs.TotalSeconds}s exceeded {_actionLimitMs / 1000}s. " +
                      $"Target=({targetX:0},{targetY:0}). Mode={_opt.Mode}");
        }

        IReadOnlyList<Input.PathPoint> path;
        try
        {
            path = _path.GeneratePath(targetX, targetY, _rng, _opt);
        }
        catch (Exception ex)
        {
            _log.Error("Path generation failed.", ex);
            return;
        }

        if (path.Count == 0)
        {
            if (_opt.LogActions) _log.Info("ACTION: generated empty path; skipping.");
            return;
        }

        // Execute path
        try
        {
            if (_opt.Mode == JiggleMode.Batch)
            {
                // Batch: constant delay (ms) between steps, ignore per-point delays
                foreach (var pt in path)
                {
                    ct.ThrowIfCancellationRequested();
                    await _input.MoveMouseAsync(pt.Dx, pt.Dy, ct).ConfigureAwait(false);

                    if (_opt.BatchStepDelayMs > 0)
                        await Task.Delay(TimeSpan.FromMilliseconds(_opt.BatchStepDelayMs), ct).ConfigureAwait(false);
                }
            }
            else // Smooth
            {
                foreach (var pt in path)
                {
                    ct.ThrowIfCancellationRequested();
                    await _input.MoveMouseAsync(pt.Dx, pt.Dy, ct).ConfigureAwait(false);

                    // If your generator already puts randomized delay values in each point,
                    // we honor them here; otherwise you can choose a fixed smooth delay.
                    int delayUs = pt.DelayUs > 0 ? pt.DelayUs : _opt.SmoothFallbackDelayUs;
                    if (delayUs > 0)
                        await DelayMicrosecondsAsync(delayUs, ct).ConfigureAwait(false);
                }
            }

            if (_opt.LogActions)
                _log.Info($"ACTION: executed {path.Count} points.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Error("Input injection failed during action.", ex);
        }
    }

    private void ResetActionLimit()
    {
        // actionLimit = random between MinAction and MaxAction
        var min = _opt.MinActionMs;
        var max = _opt.MaxActionMs;
        if (min < 0) min = 0;
        if (max < min) max = min;

        long next = (max == min)
            ? min
            : min + _rng.NextInt64(0, (max - min) + 1);

        _actionLimitMs = next;

        if (_opt.LogNextTrigger)
            _log.Info($"Next trigger: {_actionLimitMs / 1000}s");
    }

    private double RandF(double min, double max)
    {
        if (max < min) (min, max) = (max, min);
        return min + _rng.NextDouble() * (max - min);
    }

    private static Task DelayMicrosecondsAsync(int microseconds, CancellationToken ct)
    {
        if (microseconds <= 0) return Task.CompletedTask;

        // Good enough for “human-ish” mouse movement. Linux scheduling granularity varies.
        // Convert to TimeSpan with fractional ms.
        double ms = microseconds / 1000.0;
        return Task.Delay(TimeSpan.FromMilliseconds(ms), ct);
    }
}