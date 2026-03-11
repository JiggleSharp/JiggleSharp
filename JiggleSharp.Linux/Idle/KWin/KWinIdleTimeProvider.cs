using JiggleSharp.Core.Idle;
using Tmds.DBus;
using System.Timers;
using Timer = System.Timers.Timer;

namespace JiggleSharp.Linux.Idle.KWin;

/// <summary>
/// Implements <see cref="IIdleTimeProvider"/> using the KWin Wayland idle
/// protocol via <see cref="KWinIdleMonitor"/>.
///
/// How it works:
///   - <see cref="KWinIdleMonitor"/> connects directly to the Wayland compositor
///     and fires <c>BecameIdle</c> / <c>Resumed</c> events at the 1-second
///     threshold boundary.
///   - Because the compositor only fires those two edge events, a 125 ms
///     <see cref="Timer"/> is used to interpolate the growing idle duration
///     between them, keeping <see cref="IdleTimeChanged"/> subscribers updated
///     at a smooth polling rate while the user remains idle.
///   - The timer is started when idle begins and stopped as soon as activity
///     resumes, so it never runs unnecessarily.
/// </summary>
public class KWinIdleTimeProvider : IIdleTimeProvider, IDisposable
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimum inactivity duration before the KWin compositor fires the idle
    /// event. A 1-second threshold keeps reported idle time closely aligned
    /// with actual user inactivity.
    /// </summary>
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often the interpolation timer fires while the user is idle.
    /// 125 ms gives ~8 updates/second — smooth enough for UI feedback without
    /// being expensive.
    /// </summary>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(125);

    // -------------------------------------------------------------------------
    // Core components
    // -------------------------------------------------------------------------

    /// <summary>
    /// Low-level Wayland monitor that communicates with the KWin compositor
    /// to receive idle/resumed edge events.
    /// </summary>
    private readonly KWinIdleMonitor _kwinMonitor = new(IdleThreshold);

    /// <summary>
    /// Fires repeatedly at <see cref="PollingInterval"/> while the user is idle
    /// to interpolate <see cref="_currentIdleTime"/> and raise
    /// <see cref="IdleTimeChanged"/>.
    /// </summary>
    private readonly Timer _timer = new(PollingInterval);

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Controls the lifetime of any async work started by <see cref="Start"/>.
    /// Replaced with a fresh instance on each <see cref="Start"/> call so the
    /// provider can be restarted after a <see cref="StopAsync"/>.
    /// </summary>
    private CancellationTokenSource _cts = new();

    // -------------------------------------------------------------------------
    // Idle state
    // -------------------------------------------------------------------------

    /// <summary>Whether the compositor has reported the user as currently idle.</summary>
    private bool _isIdle;

    /// <summary>
    /// UTC timestamp recorded when <c>BecameIdle</c> fired. Used by the timer
    /// to compute elapsed time since the idle event.
    /// </summary>
    private DateTime _idleStartedUtc;

    /// <summary>
    /// The most recently computed idle duration. Zero while the user is active;
    /// ≥ <see cref="IdleThreshold"/> while idle.
    /// </summary>
    private TimeSpan _currentIdleTime = TimeSpan.Zero;

    // =========================================================================
    // IIdleTimeProvider — public API
    // =========================================================================

    /// <inheritdoc/>
    public event EventHandler<IdleTimeChangedEventArgs>? IdleTimeChanged;

    /// <summary>
    /// Probes whether the KWin idle protocol is available on the current
    /// Wayland compositor. Returns <c>false</c> on any error, including when
    /// not running under a KWin/Wayland session.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            return await _kwinMonitor.DetermineAvailability(_cts.Token);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the current idle time snapshot. Returns
    /// <see cref="TimeSpan.Zero"/> while the user is active, and a value ≥
    /// <see cref="IdleThreshold"/> while idle.
    /// </summary>
    public async Task<TimeSpan> GetIdleTimeAsync(CancellationToken ct = default)
    {
        return await Task.Run(() => _currentIdleTime, ct);
    }

    /// <summary>
    /// Connects to the Wayland compositor, begins monitoring for idle events,
    /// and starts the interpolation timer. Safe to call after
    /// <see cref="StopAsync"/> to restart monitoring.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();

        _kwinMonitor.BecameIdle += KwinMonitorOnBecameIdle;
        _kwinMonitor.Resumed    += KwinMonitorOnResumed;
        _timer.Elapsed          += TimerElapsed;

        _kwinMonitor.Start();
    }

    /// <summary>
    /// Cancels the active monitoring session and releases the
    /// <see cref="CancellationTokenSource"/>. Does not dispose the underlying
    /// Wayland monitor; call <see cref="Dispose"/> for full teardown.
    /// </summary>
    public async Task StopAsync()
    {
        await _cts.CancelAsync();

        try
        {
            _timer.Stop();
            _kwinMonitor.BecameIdle -= KwinMonitorOnBecameIdle;
            _kwinMonitor.Resumed    -= KwinMonitorOnResumed;
            _timer.Elapsed          -= TimerElapsed;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _cts.Dispose();
            _cts = null!;
        }
    }

    // =========================================================================
    // IDisposable
    // =========================================================================

    public void Dispose()
    {
        _timer.Dispose();
        _kwinMonitor.Dispose();
        GC.SuppressFinalize(this);
    }

    // =========================================================================
    // Event handlers — KWinIdleMonitor
    // =========================================================================

    /// <summary>
    /// Called by <see cref="KWinIdleMonitor"/> when the compositor fires the
    /// idle event (i.e. no input for ≥ <see cref="IdleThreshold"/>).
    ///
    /// We record the timestamp, seed <see cref="_currentIdleTime"/> with the
    /// threshold (the minimum known idle duration at this moment), then start
    /// the interpolation timer to grow the value over time.
    /// </summary>
    private void KwinMonitorOnBecameIdle(object? sender, EventArgs e)
    {
        _isIdle         = true;
        _idleStartedUtc = DateTime.UtcNow;

        // At the moment the idle event fires, the user has already been idle
        // for at least IdleThreshold — seed with that minimum rather than zero.
        _currentIdleTime = IdleThreshold;

        _timer.Start();
        IdleTimeChanged?.Invoke(this, new IdleTimeChangedEventArgs(_currentIdleTime));
    }

    /// <summary>
    /// Called by <see cref="KWinIdleMonitor"/> when the compositor reports that
    /// user activity has resumed.
    ///
    /// Stops the interpolation timer and resets idle state to zero.
    /// </summary>
    private void KwinMonitorOnResumed(object? sender, EventArgs e)
    {
        _isIdle = false;
        _timer.Stop();

        _currentIdleTime = TimeSpan.Zero;
        IdleTimeChanged?.Invoke(this, new IdleTimeChangedEventArgs(_currentIdleTime));
    }

    // =========================================================================
    // Event handlers — interpolation timer
    // =========================================================================

    /// <summary>
    /// Fires every <see cref="PollingInterval"/> while the user is idle.
    /// Computes the current idle duration as:
    ///   <see cref="IdleThreshold"/> + (time elapsed since the idle event fired)
    /// and raises <see cref="IdleTimeChanged"/> with the updated value.
    ///
    /// The guard on <see cref="_isIdle"/> is a safety check against a race
    /// where the timer fires just after <c>Resumed</c> has been handled but
    /// before <see cref="Timer.Stop"/> takes effect.
    /// </summary>
    private void TimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!_isIdle)
            return;

        var elapsedSinceIdleEvent = DateTime.UtcNow - _idleStartedUtc;
        _currentIdleTime = IdleThreshold + elapsedSinceIdleEvent;

        IdleTimeChanged?.Invoke(this, new IdleTimeChangedEventArgs(_currentIdleTime));
    }
}
