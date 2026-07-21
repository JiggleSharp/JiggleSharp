using JiggleSharp.Core.Input;
using JiggleSharp.Linux.DBusInterfaces;
using Serilog;
using Tmds.DBus;

namespace JiggleSharp.Linux.Input;

/// <summary>
/// Implements <see cref="IInputInjector"/> by driving the freedesktop.org
/// RemoteDesktop portal (<c>org.freedesktop.portal.RemoteDesktop</c>) directly
/// over D-Bus. There is no external process dependency (unlike the previous
/// <c>ydotool</c>-based injector).
///
/// How it works:
///   1. On first use, a portal session is established and pointer access is
///      requested: <c>CreateSession</c> → <c>SelectDevices</c> → <c>Start</c>.
///      <c>Start</c> triggers a one-time consent dialog; the user must
///      approve it before mouse movement will work.
///   2. Once started, each move is sent via <c>NotifyPointerMotion</c> on the
///      already-established session — no further prompts.
///   3. Portal request methods (<c>CreateSession</c>, <c>SelectDevices</c>,
///      <c>Start</c>) return a request object path; the actual result
///      arrives asynchronously via a <c>Response</c> signal on that object.
///      The signal is subscribed to on the caller-predictable request path
///      (derived from a <c>handle_token</c>, per the portal spec) *before*
///      the request method is invoked, so a response that arrives
///      immediately after the call cannot race past the subscription.
/// </summary>
public sealed class PortalInputInjector : IInputInjector, IDisposable
{
    // -------------------------------------------------------------------------
    // Constants — D-Bus addressing
    // -------------------------------------------------------------------------

    private const string PortalBusName = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath PortalObjectPath = new("/org/freedesktop/portal/desktop");
    private const string RequestPathPrefix = "/org/freedesktop/portal/desktop/request";

    /// <summary>Device type bitmask value for pointer devices (portal spec).</summary>
    private const uint PointerDeviceType = 2;

    /// <summary>Request <c>Response</c> signal code indicating success.</summary>
    private const uint ResponseSuccess = 0;

    /// <summary>Request <c>Response</c> signal code indicating the user cancelled/denied.</summary>
    private const uint ResponseCancelled = 1;

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    /// <summary>Guards the one-time session-establishment handshake.</summary>
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Session bus connection. Created lazily on first use and held open for
    /// the lifetime of this instance.
    /// </summary>
    private Connection? _connection;

    /// <summary>
    /// This connection's own unique D-Bus name, captured from
    /// <see cref="Connection.ConnectAsync"/>. Used to predict portal request
    /// object paths ahead of issuing the request that creates them.
    /// </summary>
    private string? _localName;

    /// <summary>
    /// Proxy for the well-known portal object, cached once the connection is
    /// established so repeated <c>NotifyPointerMotion</c> calls don't
    /// re-create it on every move.
    /// </summary>
    private IRemoteDesktop? _remoteDesktop;

    /// <summary>Handle of the established RemoteDesktop session, once created.</summary>
    private ObjectPath? _sessionHandle;

    /// <summary>
    /// <c>true</c> once <c>Start</c> has succeeded and
    /// <c>NotifyPointerMotion</c> calls are safe to make.
    /// </summary>
    private bool _sessionStarted;

    private bool _disposed;

    // =========================================================================
    // IInputInjector
    // =========================================================================

    public event EventHandler<Exception>? InputInjectorFailure;

    /// <summary>
    /// Proactively establishes the RemoteDesktop portal session, triggering
    /// the one-time consent dialog immediately (e.g. when the jiggle engine
    /// starts) rather than waiting for the first <see cref="MoveMouseAsync"/>
    /// call. Never throws: a denied or failed handshake is reported via
    /// <see cref="InputInjectorFailure"/> instead. A no-op if the session is
    /// already established.
    /// </summary>
    public Task RequestPermissionAsync(CancellationToken ct) => TryEnsureSessionReadyAsync(ct);

    /// <summary>
    /// Moves the mouse by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// pixels via the RemoteDesktop portal, establishing the portal session
    /// first if it hasn't been already (e.g. via <see cref="RequestPermissionAsync"/>).
    ///
    /// Never throws: failures (including a denied consent dialog) are
    /// reported via <see cref="InputInjectorFailure"/> instead, matching the
    /// contract of the other platform injectors.
    /// </summary>
    public async Task MoveMouseAsync(int dx, int dy, CancellationToken ct)
    {
        if (!await TryEnsureSessionReadyAsync(ct).ConfigureAwait(false))
            return; // failure already reported via InputInjectorFailure

        try
        {
            await _remoteDesktop!.NotifyPointerMotionAsync(
                _sessionHandle!.Value,
                new Dictionary<string, object>(),
                dx,
                dy).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var error = new InputInjectorException($"An error occurred while moving the mouse: {ex.Message}", ex);
            Log.Error(error.Message);
            InputInjectorFailure?.Invoke(this, error);
        }
    }

    /// <summary>
    /// Calls <see cref="EnsureSessionReadyAsync"/>, converting any failure
    /// (including a denied consent dialog) into an <see cref="InputInjectorFailure"/>
    /// notification rather than an exception. Shared by
    /// <see cref="RequestPermissionAsync"/> and <see cref="MoveMouseAsync"/>.
    /// </summary>
    /// <returns><c>true</c> if the session is ready; <c>false</c> if it failed.</returns>
    private async Task<bool> TryEnsureSessionReadyAsync(CancellationToken ct)
    {
        try
        {
            await EnsureSessionReadyAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            var error = new InputInjectorException(
                $"RemoteDesktop portal session could not be established: {ex.Message}", ex);
            Log.Error(error.Message);
            InputInjectorFailure?.Invoke(this, error);
            return false;
        }
    }

    // =========================================================================
    // Session establishment
    // =========================================================================

    /// <summary>
    /// Ensures a RemoteDesktop portal session exists and pointer access has
    /// been granted, performing the one-time <c>CreateSession</c> →
    /// <c>SelectDevices</c> → <c>Start</c> handshake if needed.
    ///
    /// If any step fails (including the user denying the consent dialog
    /// shown by <c>Start</c>), any partially-created session is closed and
    /// the failure is rethrown to the caller. Session state is not "sticky":
    /// a later call retries the handshake from scratch, so a user who denies
    /// consent by mistake can grant it on a subsequent attempt (e.g. after
    /// restarting the jiggle engine) rather than being locked out permanently.
    /// </summary>
    private async Task EnsureSessionReadyAsync(CancellationToken ct)
    {
        if (_sessionStarted)
            return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_sessionStarted)
                return;

            var isNewConnection = _connection is null;
            _connection ??= new Connection(Address.Session);
            if (isNewConnection)
            {
                var connectionInfo = await _connection.ConnectAsync().ConfigureAwait(false);
                _localName = connectionInfo.LocalName;
                _remoteDesktop = _connection.CreateProxy<IRemoteDesktop>(PortalBusName, PortalObjectPath);
            }

            try
            {
                var sessionHandle = await CreateSessionAsync(ct).ConfigureAwait(false);
                _sessionHandle = sessionHandle;

                await SelectDevicesAsync(sessionHandle, ct).ConfigureAwait(false);
                await StartAsync(sessionHandle, ct).ConfigureAwait(false);

                _sessionStarted = true;
            }
            catch
            {
                await CloseSessionQuietlyAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<ObjectPath> CreateSessionAsync(CancellationToken ct)
    {
        var sessionHandleToken = NewToken();

        var (response, results) = await InvokeRequestAsync(
            handleToken => _remoteDesktop!.CreateSessionAsync(new Dictionary<string, object>
            {
                ["handle_token"] = handleToken,
                ["session_handle_token"] = sessionHandleToken
            }),
            ct).ConfigureAwait(false);

        if (response != ResponseSuccess)
            throw new InputInjectorException(
                $"RemoteDesktop portal session could not be created (response code {response}).");

        // Per the portal spec, session_handle is returned as a string (type
        // 's'), even though its value is formatted as an object path.
        if (!results.TryGetValue("session_handle", out var raw) || raw is not string sessionHandleString)
            throw new InputInjectorException(
                "RemoteDesktop portal CreateSession response did not include a session_handle.");

        return new ObjectPath(sessionHandleString);
    }

    private async Task SelectDevicesAsync(ObjectPath sessionHandle, CancellationToken ct)
    {
        var (response, _) = await InvokeRequestAsync(
            handleToken => _remoteDesktop!.SelectDevicesAsync(sessionHandle, new Dictionary<string, object>
            {
                ["handle_token"] = handleToken,
                ["types"] = PointerDeviceType
            }),
            ct).ConfigureAwait(false);

        if (response != ResponseSuccess)
            throw new InputInjectorException(
                $"RemoteDesktop portal device selection failed (response code {response}).");
    }

    /// <summary>
    /// Requests that the compositor grant the devices selected in
    /// <see cref="SelectDevicesAsync"/>. This is what actually shows the
    /// one-time consent dialog to the user.
    /// </summary>
    private async Task StartAsync(ObjectPath sessionHandle, CancellationToken ct)
    {
        var (response, _) = await InvokeRequestAsync(
            handleToken => _remoteDesktop!.StartAsync(sessionHandle, string.Empty, new Dictionary<string, object>
            {
                ["handle_token"] = handleToken
            }),
            ct).ConfigureAwait(false);

        if (response == ResponseCancelled)
            throw new InputInjectorException(
                "Remote control access was denied. Grant permission in the consent dialog to allow " +
                "JiggleSharp to move the mouse.");

        if (response != ResponseSuccess)
            throw new InputInjectorException(
                $"RemoteDesktop portal session could not be started (response code {response}).");
    }

    // =========================================================================
    // Request/Response plumbing
    // =========================================================================

    /// <summary>
    /// Issues a portal request method that returns a request object path,
    /// and awaits its result from the <c>Response</c> signal on that object.
    ///
    /// The signal subscription is established on the *predicted* request
    /// path (derived from a caller-generated <c>handle_token</c>, per the
    /// portal spec) before <paramref name="invoke"/> is called, so a
    /// response that arrives immediately after the method call cannot be
    /// missed.
    /// </summary>
    private async Task<(uint Response, IDictionary<string, object> Results)> InvokeRequestAsync(
        Func<string, Task<ObjectPath>> invoke, CancellationToken ct)
    {
        var connection = _connection!;
        var handleToken = NewToken();
        var sender = SenderSegment(_localName!);
        var expectedPath = new ObjectPath($"{RequestPathPrefix}/{sender}/{handleToken}");

        var tcs = new TaskCompletionSource<(uint, IDictionary<string, object>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var requestProxy = connection.CreateProxy<IRequest>(PortalBusName, expectedPath);
        using var subscription = await requestProxy
            .WatchResponseAsync(result => tcs.TrySetResult(result))
            .ConfigureAwait(false);

        // Subscription is now registered on the bus; safe to issue the request.
        await invoke(handleToken).ConfigureAwait(false);

        using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Best-effort close of the current session, if one was created. Swallows
    /// all errors since this is used both during cleanup after a failed
    /// handshake and during application shutdown.
    /// </summary>
    private async Task CloseSessionQuietlyAsync()
    {
        if (_sessionHandle is not { } sessionHandle || _connection is null)
            return;

        try
        {
            var session = _connection.CreateProxy<ISession>(PortalBusName, sessionHandle);
            await session.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to close RemoteDesktop portal session cleanly: {ex.Message}");
        }
        finally
        {
            _sessionHandle = null;
            _sessionStarted = false;
        }
    }

    private static string NewToken() => "jsp_" + Guid.NewGuid().ToString("N");

    private static string SenderSegment(string uniqueName) => uniqueName.TrimStart(':').Replace('.', '_');

    // =========================================================================
    // IDisposable
    // =========================================================================

    /// <summary>
    /// Closes the portal session (if one was established) and disposes the
    /// D-Bus connection. Safe to call multiple times, and from either the
    /// normal application-exit path or the tray "Quit" handler.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // Run on the thread pool: this may be called from the UI thread,
            // and blocking on a Task that captured a synchronization context
            // could otherwise deadlock.
            Task.Run(CloseSessionQuietlyAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to close RemoteDesktop portal session during shutdown: {ex.Message}");
        }

        _connection?.Dispose();
        _connection = null;
        _remoteDesktop = null;
        _initLock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PortalInputInjector));
    }
}
