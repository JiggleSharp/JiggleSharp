using JiggleSharp.Core.Idle;
using Tmds.DBus;

namespace JiggleSharp.Linux.Idle.Mutter;

/// <summary>
/// Implements <see cref="IIdleTimeProvider"/> by polling the
/// <c>org.gnome.Mutter.IdleMonitor</c> D-Bus service once per second.
///
/// How it works:
///   - Unlike the KWin path (which receives compositor push-events),
///     Mutter exposes a single <c>GetIdletime</c> RPC that returns the
///     cumulative milliseconds since the last input event. There is no
///     subscription / watch mechanism, so we poll on a 1-second
///     <see cref="PeriodicTimer"/> and raise <see cref="IdleTimeChanged"/>
///     on each tick.
///   - The D-Bus connection is managed by <see cref="MutterIdleMonitorDbus"/>,
///     which handles lazy initialization and caches the proxy.
///   - The polling loop runs on the thread-pool via <c>Task.Run</c> and is
///     cancelled cleanly through a <see cref="CancellationTokenSource"/> when
///     <see cref="StopAsync"/> is called.
/// </summary>
public class MutterIdleTimeProvider : IIdleTimeProvider, IDisposable
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// How often <see cref="LoopAsync"/> polls Mutter for the current idle time.
    /// One second matches the granularity most callers care about and keeps
    /// D-Bus traffic negligible.
    /// </summary>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound applied to the raw value returned by Mutter to guard against
    /// corrupt or nonsensical readings (e.g. an uninitialized uint64).
    /// 10 years is effectively "infinity" for idle-time purposes.
    /// </summary>
    private static readonly TimeSpan MaxIdleTime = TimeSpan.FromDays(3650);

    // -------------------------------------------------------------------------
    // Core components
    // -------------------------------------------------------------------------

    /// <summary>
    /// Owns the D-Bus session connection and the <c>org.gnome.Mutter.IdleMonitor</c>
    /// proxy. Handles lazy initialization and is safe to call from multiple
    /// concurrent callers.
    /// </summary>
    private readonly MutterIdleMonitorDbus _dbus = new();

    // -------------------------------------------------------------------------
    // Polling loop state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signals the polling loop to stop. Null when the provider is not running.
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The running <see cref="LoopAsync"/> task. Null when the provider is not
    /// running. Awaited in <see cref="StopAsync"/> to ensure a clean shutdown.
    /// </summary>
    private Task? _loop;

    // =========================================================================
    // IIdleTimeProvider — public API
    // =========================================================================

    /// <inheritdoc/>
    public event EventHandler<IdleTimeChangedEventArgs>? IdleTimeChanged;

    /// <summary>
    /// Checks whether the Mutter idle monitor service is reachable by attempting
    /// to obtain the D-Bus proxy. Returns <c>false</c> on any error, including
    /// when not running under GNOME Shell / Mutter.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            _ = await _dbus.GetProxyAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Queries Mutter directly for the current idle time and raises
    /// <see cref="IdleTimeChanged"/> with the result.
    ///
    /// The raw value from <c>GetIdletime</c> is a uint64 of milliseconds.
    /// It is clamped to <see cref="MaxIdleTime"/> before being returned to
    /// guard against corrupt readings.
    ///
    /// Returns <see cref="TimeSpan.Zero"/> when the Mutter service is
    /// unavailable, not owned, or access is denied — situations that indicate
    /// the provider should not be used rather than that the user is active.
    /// </summary>
    public async Task<TimeSpan> GetIdleTimeAsync(CancellationToken ct = default)
    {
        try
        {
            var proxy = await _dbus.GetProxyAsync().ConfigureAwait(false);

            // GetIdletimeAsync returns uint64 milliseconds. Common tooling
            // (e.g. xprintidle equivalents on Wayland) treats this as ms.
            ulong idleMs = await proxy.GetIdletimeAsync().ConfigureAwait(false);

            // Clamp to MaxIdleTime to guard against corrupt / uninitialized values.
            ulong maxMs = (ulong)MaxIdleTime.TotalMilliseconds;
            if (idleMs > maxMs)
                idleMs = maxMs;

            var idleTime = TimeSpan.FromMilliseconds(idleMs);
            IdleTimeChanged?.Invoke(this, new IdleTimeChangedEventArgs(idleTime));
            return idleTime;
        }
        catch (DBusException ex) when (
            ex.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown"
                         or "org.freedesktop.DBus.Error.NameHasNoOwner"
                         or "org.freedesktop.DBus.Error.AccessDenied")
        {
            // Mutter is not running, the service has disappeared, or a sandbox
            // policy is blocking access. Treat as "not available" rather than
            // an error worth propagating.
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Starts the background polling loop. Idempotent — does nothing if the
    /// loop is already running.
    /// </summary>
    public void Start()
    {
        if (_loop != null)
            return;

        _cts  = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    /// <summary>
    /// Cancels the polling loop, waits for it to exit, and resets loop state.
    /// Safe to call when the provider is not running.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        await _cts.CancelAsync();

        try
        {
            if (_loop != null)
                await _loop;
        }
        catch (OperationCanceledException) { /* expected on cancellation */ }
        finally
        {
            _loop = null;
            _cts.Dispose();
            _cts = null;
        }
    }

    // =========================================================================
    // IDisposable
    // =========================================================================

    public void Dispose()
    {
        _dbus.Dispose();
        GC.SuppressFinalize(this);
    }

    // =========================================================================
    // Private — polling loop
    // =========================================================================

    /// <summary>
    /// Polls <see cref="GetIdleTimeAsync"/> once per <see cref="PollingInterval"/>
    /// until <paramref name="ct"/> is cancelled.
    ///
    /// Uses <see cref="PeriodicTimer"/> rather than <c>Task.Delay</c> in a loop
    /// to avoid timer drift: <see cref="PeriodicTimer"/> fires relative to the
    /// previous tick rather than the end of the previous iteration.
    /// </summary>
    private async Task LoopAsync(CancellationToken ct = default)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            _ = await GetIdleTimeAsync(ct);
        }
    }
}
