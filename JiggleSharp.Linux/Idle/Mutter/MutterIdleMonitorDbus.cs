using JiggleSharp.Linux.DBusInterfaces;
using Tmds.DBus;

namespace JiggleSharp.Linux.Idle.Mutter;

/// <summary>
/// Manages a lazy, thread-safe D-Bus connection to the
/// <c>org.gnome.Mutter.IdleMonitor</c> service and vends a ready-to-use
/// <see cref="IMutterIdleMonitor"/> proxy.
///
/// How it works:
///   - The D-Bus session connection and proxy are created on first use
///     (lazy initialization) rather than at construction time, so the cost
///     is only paid if the Mutter idle path is actually exercised.
///   - A <see cref="SemaphoreSlim"/> ensures that concurrent callers race to
///     initialize exactly once; subsequent callers receive the cached proxy
///     without re-entering initialization.
///   - Before creating the proxy, the service name is verified with
///     <c>org.freedesktop.DBus.NameHasOwner</c> so that a clear, actionable
///     exception is thrown if GNOME Shell / Mutter is not running.
/// </summary>
internal sealed class MutterIdleMonitorDbus : IDisposable
{
    // -------------------------------------------------------------------------
    // Constants — D-Bus addressing
    // -------------------------------------------------------------------------

    /// <summary>Well-known D-Bus service name for the Mutter idle monitor.</summary>
    private const string ServiceName = "org.gnome.Mutter.IdleMonitor";

    /// <summary>
    /// Object path for the "Core" idle monitor instance, which tracks the
    /// cumulative idle time across all input devices.
    /// </summary>
    private static readonly ObjectPath CoreObjectPath =
        new("/org/gnome/Mutter/IdleMonitor/Core");

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    /// <summary>
    /// Session bus connection. Created lazily on first <see cref="GetProxyAsync"/>
    /// call and held open for the lifetime of this instance so the proxy remains valid.
    /// </summary>
    private Connection? _session;

    /// <summary>
    /// Cached proxy for <c>org.gnome.Mutter.IdleMonitor</c>.
    /// Null until the first successful call to <see cref="GetProxyAsync"/>.
    /// </summary>
    private IMutterIdleMonitor? _proxy;

    /// <summary>
    /// Guards the lazy-initialization block so that only one concurrent caller
    /// runs the connect + proxy-create sequence at a time.
    /// </summary>
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private bool _disposed;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Returns a connected, ready-to-use <see cref="IMutterIdleMonitor"/> proxy.
    ///
    /// On the first call this method:
    ///   1. Opens the D-Bus session bus connection.
    ///   2. Confirms that <c>org.gnome.Mutter.IdleMonitor</c> is present on the bus.
    ///   3. Creates and caches the proxy.
    ///
    /// Subsequent calls return the cached proxy immediately (the double-checked
    /// lock means no semaphore acquisition after initialization).
    /// </summary>
    /// <exception cref="ObjectDisposedException">If this instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// If the Mutter idle monitor service is not available on the session bus
    /// (i.e. GNOME Shell / Mutter is not running).
    /// </exception>
    public async Task<IMutterIdleMonitor> GetProxyAsync()
    {
        // Fast path: proxy already initialized, no locking needed.
        if (_proxy is not null)
            return _proxy;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            // Second check inside the lock in case another caller initialized
            // while we were waiting for the semaphore.
            if (_proxy is not null)
                return _proxy;

            // Open the session bus connection if not already open.
            // Note: this version of Tmds.DBus does not expose a CT overload
            // for ConnectAsync, so cancellation is not supported here.
            _session ??= new Connection(Address.Session);
            await _session.ConnectAsync().ConfigureAwait(false);

            // Verify the service is present before attempting to create the
            // proxy, so callers receive a clear error rather than a silent
            // failure or timeout.
            if (!await IsServiceAvailableAsync(_session, ServiceName).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"DBus service '{ServiceName}' not available on the session bus. " +
                    "Are you running GNOME Shell / Mutter?");
            }

            _proxy = _session.CreateProxy<IMutterIdleMonitor>(ServiceName, CoreObjectPath);
            return _proxy;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // =========================================================================
    // IDisposable
    // =========================================================================

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _proxy    = null;

        _session?.Dispose();
        _session = null;

        _initLock.Dispose();
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Asks the D-Bus daemon whether <paramref name="name"/> currently has an
    /// owner on <paramref name="connection"/>, which is the canonical way to
    /// check whether a service is running without attempting to start it.
    /// </summary>
    private static async Task<bool> IsServiceAvailableAsync(Connection connection, string name)
    {
        var dbus = connection.CreateProxy<IOrgFreedesktopDBus>(
            "org.freedesktop.DBus",
            "/org/freedesktop/DBus");

        return await dbus.NameHasOwnerAsync(name).ConfigureAwait(false);
    }

    /// <summary>Throws <see cref="ObjectDisposedException"/> if this instance has been disposed.</summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MutterIdleMonitorDbus));
    }
}
