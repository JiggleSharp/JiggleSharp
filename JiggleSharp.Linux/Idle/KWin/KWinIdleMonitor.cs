using System.Runtime.InteropServices;
using System.Text;

namespace JiggleSharp.Linux.Idle.KWin;

/// <summary>
/// Monitors user idle time on KDE/KWin Wayland compositors using the
/// <c>org_kde_kwin_idle</c> Wayland protocol extension.
///
/// How it works:
///   1. Connects to the Wayland display server and enumerates its global
///      objects (registry roundtrip).
///   2. Binds <c>wl_seat</c> (the input device group) and
///      <c>org_kde_kwin_idle</c> (the KWin idle manager).
///   3. Asks the idle manager to create an <c>org_kde_kwin_idle_timeout</c>
///      for the requested threshold in milliseconds.
///   4. The compositor fires the <c>idle</c> event on that timeout object
///      when no input has been seen for the threshold period, and a
///      <c>resumed</c> event when input resumes.
///   5. A background thread dispatches Wayland events on a 250 ms poll
///      cycle so the CLR side stays responsive to Dispose().
/// </summary>
public sealed class KWinIdleMonitor : IDisposable
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const string WaylandLib = "libwayland-client.so.0";

    // Wayland wire-protocol opcode indices (zero-based, per interface).
    // These match the order the messages appear in the protocol XML.
    private const uint WlDisplayGetRegistry         = 1;   // wl_display::get_registry
    private const uint WlRegistryBind               = 0;   // wl_registry::bind
    private const uint OrgKdeKwinIdleGetIdleTimeout = 0;   // org_kde_kwin_idle::get_idle_timeout
    private const uint OrgKdeKwinIdleTimeoutRelease = 0;   // org_kde_kwin_idle_timeout::release

    /// <summary>
    /// Passed to <c>wl_proxy_marshal_flags</c> to indicate that marshalling
    /// this request should also destroy the proxy (protocol destructor).
    /// </summary>
    private const uint WlMarshalFlagDestroy = 1;

    // -------------------------------------------------------------------------
    // Fields — configuration & synchronisation
    // -------------------------------------------------------------------------

    private readonly TimeSpan _threshold;

    /// <summary>Guards Start() and Dispose() for thread-safety.</summary>
    private readonly object _sync = new();

    // -------------------------------------------------------------------------
    // Fields — Wayland object handles
    // -------------------------------------------------------------------------

    /// <summary>Connection to the Wayland display socket.</summary>
    private IntPtr _display;

    /// <summary>Registry proxy used to enumerate compositor globals.</summary>
    private IntPtr _registry;

    /// <summary>wl_seat: represents the user's input devices.</summary>
    private IntPtr _seat;

    /// <summary>org_kde_kwin_idle: KWin idle manager global.</summary>
    private IntPtr _idleManager;

    /// <summary>org_kde_kwin_idle_timeout: per-threshold idle notification object.</summary>
    private IntPtr _idleTimeout;

    // -------------------------------------------------------------------------
    // Fields — GC-pinned self reference
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pins <c>this</c> so that the unmanaged listener callbacks (which receive
    /// the pointer as <c>data</c>) can call <see cref="GCHandle.FromIntPtr"/>
    /// to recover the managed instance without risking the GC moving it.
    /// </summary>
    private GCHandle _selfHandle;

    // -------------------------------------------------------------------------
    // Fields — listener delegates & native vtable memory
    // -------------------------------------------------------------------------

    // Delegates must be kept alive (not just the function pointers) for the
    // duration of the listener's registration, otherwise the GC may collect
    // them while unmanaged code still holds the function pointer.

    private RegistryGlobalDelegate?       _registryGlobal;
    private RegistryGlobalRemoveDelegate? _registryGlobalRemove;
    /// <summary>
    /// Unmanaged memory block acting as the wl_registry listener vtable:
    /// [ ptr-to-global, ptr-to-global_remove ]
    /// </summary>
    private IntPtr _registryListenerTable;

    private IdleDelegate?   _idleDelegate;
    private ResumedDelegate? _resumedDelegate;
    /// <summary>
    /// Unmanaged memory block acting as the org_kde_kwin_idle_timeout listener
    /// vtable: [ ptr-to-idle, ptr-to-resumed ]
    /// </summary>
    private IntPtr _timeoutListenerTable;

    // -------------------------------------------------------------------------
    // Fields — event dispatch thread
    // -------------------------------------------------------------------------

    private Thread? _eventThread;
    private volatile bool _running;
    private volatile bool _disposed;

    // -------------------------------------------------------------------------
    // Fields — idle state
    // -------------------------------------------------------------------------

    private bool _isIdle;
    private DateTimeOffset? _idleBeganAtUtc;

    // =========================================================================
    // Construction
    // =========================================================================

    public KWinIdleMonitor(TimeSpan threshold)
    {
        if (threshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(threshold));

        _threshold = threshold;
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>Raised on the event thread when the compositor reports idle.</summary>
    public event EventHandler? BecameIdle;

    /// <summary>Raised on the event thread when the compositor reports activity resumed.</summary>
    public event EventHandler? Resumed;

    /// <summary>Whether the compositor has reported that the user is currently idle.</summary>
    public bool IsIdle
    {
        get
        {
            ThrowIfDisposed();
            return _isIdle;
        }
    }

    /// <summary>The idle threshold that was requested from the compositor.</summary>
    public TimeSpan Threshold => _threshold;

    /// <summary>
    /// Returns <see cref="TimeSpan.Zero"/> while the user is active.
    /// Once idle, returns <c>Threshold + (time elapsed since the idle event)</c>.
    /// This will always be ≥ <c>Threshold</c> when the user is idle.
    /// </summary>
    public TimeSpan CurrentIdleTime
    {
        get
        {
            ThrowIfDisposed();

            if (!_isIdle || _idleBeganAtUtc is null)
                return TimeSpan.Zero;

            var elapsed = DateTimeOffset.UtcNow - _idleBeganAtUtc.Value;
            return _threshold + elapsed;
        }
    }

    /// <summary>
    /// Connects to Wayland, registers for idle notifications, and starts the
    /// background event-dispatch thread. Idempotent if already running.
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            if (_running)
                return;

            ConnectAndBind();
            StartEventThread();
        }
    }

    // =========================================================================
    // IDisposable
    // =========================================================================

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _running = false;
        }

        // Disconnecting the display causes the background thread's next
        // wl_display_dispatch_timeout call to return an error, naturally
        // terminating the event loop without needing a separate signal.
        if (_display != IntPtr.Zero)
        {
            WaylandNative.wl_display_disconnect(_display);
            _display = IntPtr.Zero;
        }

        if (_eventThread is { IsAlive: true })
            _eventThread.Join(TimeSpan.FromSeconds(2));

        // Send the protocol destructor for the idle timeout before destroying
        // the proxy, as required by the Wayland protocol.
        if (_idleTimeout != IntPtr.Zero)
        {
            WaylandNative.wl_proxy_marshal_flags(
                _idleTimeout,
                OrgKdeKwinIdleTimeoutRelease,
                IntPtr.Zero,
                version: 1,
                WlMarshalFlagDestroy);

            _idleTimeout = IntPtr.Zero;
        }

        if (_idleManager != IntPtr.Zero)
        {
            WaylandNative.wl_proxy_destroy(_idleManager);
            _idleManager = IntPtr.Zero;
        }

        if (_seat != IntPtr.Zero)
        {
            WaylandNative.wl_proxy_destroy(_seat);
            _seat = IntPtr.Zero;
        }

        if (_registry != IntPtr.Zero)
        {
            WaylandNative.wl_proxy_destroy(_registry);
            _registry = IntPtr.Zero;
        }

        // Free the unmanaged vtable memory allocated for each listener.
        if (_registryListenerTable != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_registryListenerTable);
            _registryListenerTable = IntPtr.Zero;
        }

        if (_timeoutListenerTable != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_timeoutListenerTable);
            _timeoutListenerTable = IntPtr.Zero;
        }

        // Release the GC pin so the GC can collect this instance freely.
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    // =========================================================================
    // Wayland setup — ConnectAndBind
    // =========================================================================

    /// <summary>
    /// Full Wayland setup sequence:
    ///   1. Connect to the display.
    ///   2. Get the global registry and listen for advertised globals.
    ///   3. First roundtrip → compositor sends us all current globals.
    ///   4. Verify wl_seat and org_kde_kwin_idle were found.
    ///   5. Create an org_kde_kwin_idle_timeout for our threshold.
    ///   6. Second roundtrip → ensure the timeout creation request has been
    ///      processed by the compositor.
    /// </summary>
    private void ConnectAndBind()
    {
        // Step 1: Open the Wayland socket (uses $WAYLAND_DISPLAY / default).
        _display = WaylandNative.wl_display_connect(null);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Could not connect to the Wayland display.");

        // Pin 'this' so callbacks can retrieve the instance via GCHandle.
        _selfHandle = GCHandle.Alloc(this);

        // Step 2: Ask the display to create a registry proxy.
        _registry = WaylandNative.wl_proxy_marshal_array_flags(
            _display,
            WlDisplayGetRegistry,
            ProtocolInterfaces.WlRegistryInterface,
            version: 1,
            flags: 0,
            args: new[] { WlArgument.FromPointer(IntPtr.Zero) }); // new_id placeholder

        if (_registry == IntPtr.Zero)
            throw new InvalidOperationException("wl_display.get_registry failed.");

        // Attach registry listeners; the compositor will call OnRegistryGlobal
        // for each currently-advertised global.
        _registryGlobal = OnRegistryGlobal;
        _registryGlobalRemove = OnRegistryGlobalRemove;
        _registryListenerTable = ListenerTableBuilder.Build(
            Marshal.GetFunctionPointerForDelegate(_registryGlobal),
            Marshal.GetFunctionPointerForDelegate(_registryGlobalRemove));

        if (WaylandNative.wl_proxy_add_listener(_registry, _registryListenerTable, GCHandle.ToIntPtr(_selfHandle)) != 0)
            throw new InvalidOperationException("wl_registry listener registration failed.");

        // Step 3: Roundtrip — blocks until compositor has processed all our
        // requests and sent back responses including the registry globals.
        RoundtripOrThrow();

        // Step 4: Verify we found what we need.
        if (_seat == IntPtr.Zero)
            throw new NotSupportedException("Wayland compositor did not advertise wl_seat.");

        if (_idleManager == IntPtr.Zero)
            throw new NotSupportedException("Wayland compositor did not advertise org_kde_kwin_idle.");

        // Step 5: Create the idle timeout object for our threshold (in ms).
        var timeoutMs = checked((uint)_threshold.TotalMilliseconds);

        _idleTimeout = WaylandNative.wl_proxy_marshal_array_flags(
            _idleManager,
            OrgKdeKwinIdleGetIdleTimeout,
            ProtocolInterfaces.OrgKdeKwinIdleTimeout.Pointer,
            version: 1,
            flags: 0,
            args: new[]
            {
                WlArgument.FromPointer(IntPtr.Zero), // new_id placeholder
                WlArgument.FromPointer(_seat),        // input seat
                WlArgument.FromUInt(timeoutMs)        // idle threshold in ms
            });

        if (_idleTimeout == IntPtr.Zero)
            throw new InvalidOperationException("org_kde_kwin_idle.get_idle_timeout failed.");

        // Register idle/resumed callbacks on the timeout object.
        _idleDelegate = OnIdle;
        _resumedDelegate = OnResumed;
        _timeoutListenerTable = ListenerTableBuilder.Build(
            Marshal.GetFunctionPointerForDelegate(_idleDelegate),
            Marshal.GetFunctionPointerForDelegate(_resumedDelegate));

        if (WaylandNative.wl_proxy_add_listener(_idleTimeout, _timeoutListenerTable, GCHandle.ToIntPtr(_selfHandle)) != 0)
            throw new InvalidOperationException("Idle timeout listener registration failed.");

        // Step 6: Second roundtrip ensures the compositor has received our
        // timeout creation request before we start the event loop.
        RoundtripOrThrow();
    }

    // =========================================================================
    // Event dispatch thread
    // =========================================================================

    private void StartEventThread()
    {
        _running = true;

        _eventThread = new Thread(EventLoop)
        {
            Name = "KWinIdleMonitor",
            IsBackground = true
        };
        _eventThread.Start();
    }

    /// <summary>
    /// Polls for Wayland events on a 250 ms cycle. The short timeout means
    /// Dispose() can tear down the connection and have this thread exit within
    /// ~250 ms without needing a more complex fd/eventfd signalling mechanism.
    /// </summary>
    private void EventLoop()
    {
        var timeout = new Timespec
        {
            tv_sec  = 0,
            tv_nsec = 250_000_000 // 250 ms in nanoseconds
        };

        while (_running && !_disposed && _display != IntPtr.Zero)
        {
            int rc = WaylandNative.wl_display_dispatch_timeout(_display, ref timeout);

            if (rc >= 0)
                continue;

            // Negative return means an error or disconnection.
            // If we're shutting down, the disconnect was intentional — exit quietly.
            if (!_running || _disposed)
                return;

            // Genuine compositor error — stop the loop.
            int err = WaylandNative.wl_display_get_error(_display);
            if (err != 0)
            {
                _running = false;
                return;
            }
        }
    }

    // =========================================================================
    // Wayland event callbacks — registry
    // =========================================================================

    /// <summary>
    /// Called (from the event thread) by the compositor for each global it
    /// advertises. We bind wl_seat and org_kde_kwin_idle on first sight.
    /// </summary>
    private static void OnRegistryGlobal(
        IntPtr data,
        IntPtr registry,
        uint   name,       // compositor-local numeric name for this global
        IntPtr interfacePtr,
        uint   version)
    {
        var self  = GetInstance(data);
        string? iface = Marshal.PtrToStringUTF8(interfacePtr);

        if (string.Equals(iface, "wl_seat", StringComparison.Ordinal) && self._seat == IntPtr.Zero)
        {
            // Bind wl_seat at v1; that is all org_kde_kwin_idle needs.
            self._seat = self.RegistryBind(registry, name, ProtocolInterfaces.WlSeatInterface, version: 1);
            return;
        }

        if (string.Equals(iface, "org_kde_kwin_idle", StringComparison.Ordinal) && self._idleManager == IntPtr.Zero)
        {
            self._idleManager = self.RegistryBind(registry, name, ProtocolInterfaces.OrgKdeKwinIdle.Pointer, version: 1);
        }
    }

    private static void OnRegistryGlobalRemove(IntPtr data, IntPtr registry, uint name)
    {
        // We don't handle runtime removal of wl_seat or org_kde_kwin_idle.
    }

    /// <summary>
    /// Issues a wl_registry::bind request, which asks the compositor to create
    /// a proxy for the global identified by <paramref name="name"/>.
    /// </summary>
    private IntPtr RegistryBind(IntPtr registry, uint name, IntPtr interfacePtr, uint version)
    {
        // wl_registry::bind arguments: name (u), interface name (s), version (u), new_id (n).
        var iface = Marshal.PtrToStructure<WlInterface>(interfacePtr);

        return WaylandNative.wl_proxy_marshal_array_flags(
            registry,
            WlRegistryBind,
            interfacePtr,
            version,
            flags: 0,
            args: new[]
            {
                WlArgument.FromUInt(name),
                WlArgument.FromPointer(iface.name), // char* interface name string
                WlArgument.FromUInt(version),
                WlArgument.FromPointer(IntPtr.Zero) // new_id placeholder
            });
    }

    // =========================================================================
    // Wayland event callbacks — idle timeout
    // =========================================================================

    /// <summary>
    /// Called by the compositor when no input has been received for the
    /// requested threshold duration.
    /// </summary>
    private static void OnIdle(IntPtr data, IntPtr timeoutProxy)
    {
        var self = GetInstance(data);

        if (self._disposed)
            return;

        self._isIdle          = true;
        self._idleBeganAtUtc  = DateTimeOffset.UtcNow;
        self.BecameIdle?.Invoke(self, EventArgs.Empty);
    }

    /// <summary>
    /// Called by the compositor when user activity resumes after an idle period.
    /// </summary>
    private static void OnResumed(IntPtr data, IntPtr timeoutProxy)
    {
        var self = GetInstance(data);

        if (self._disposed)
            return;

        self._isIdle         = false;
        self._idleBeganAtUtc = null;
        self.Resumed?.Invoke(self, EventArgs.Empty);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void RoundtripOrThrow()
    {
        int rc = WaylandNative.wl_display_roundtrip(_display);
        if (rc >= 0)
            return;

        int err = WaylandNative.wl_display_get_error(_display);
        throw new InvalidOperationException($"Wayland roundtrip failed. wl_display_get_error={err}.");
    }

    /// <summary>
    /// Recovers the <see cref="KWinIdleMonitor"/> instance from the opaque
    /// <c>data</c> pointer passed to every Wayland C callback.
    /// </summary>
    private static KWinIdleMonitor GetInstance(IntPtr data)
    {
        var handle = GCHandle.FromIntPtr(data);
        return (KWinIdleMonitor)handle.Target!;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    // =========================================================================
    // Availability probe
    // =========================================================================

    /// <summary>
    /// Performs a lightweight, non-destructive check to determine whether the
    /// current Wayland compositor supports both <c>wl_seat</c> and
    /// <c>org_kde_kwin_idle</c> without leaving any Wayland objects allocated.
    ///
    /// Safe to call before constructing and starting a full
    /// <see cref="KWinIdleMonitor"/> instance.
    /// </summary>
    public async Task<bool> DetermineAvailability(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            IntPtr display      = IntPtr.Zero;
            IntPtr registry     = IntPtr.Zero;
            IntPtr listenerTable = IntPtr.Zero;
            GCHandle? stateHandle = null;

            // Keep delegate references alive for the duration of the roundtrip.
            RegistryGlobalDelegate?       onGlobal       = null;
            RegistryGlobalRemoveDelegate? onGlobalRemove = null;

            try
            {
                display = WaylandNative.wl_display_connect(null);
                if (display == IntPtr.Zero)
                    return false;

                var state = new AvailabilityProbeState();
                stateHandle = GCHandle.Alloc(state);

                registry = WaylandNative.wl_proxy_marshal_array_flags(
                    display,
                    WlDisplayGetRegistry,
                    ProtocolInterfaces.WlRegistryInterface,
                    version: 1,
                    flags: 0,
                    args: new[] { WlArgument.FromPointer(IntPtr.Zero) });

                if (registry == IntPtr.Zero)
                    return false;

                // Probe-only callbacks: just set flags, don't bind anything.
                onGlobal = static (data, reg, name, interfacePtr, version) =>
                {
                    var handle = GCHandle.FromIntPtr(data);
                    var probe  = (AvailabilityProbeState)handle.Target!;

                    string? iface = Marshal.PtrToStringUTF8(interfacePtr);
                    if (iface is null)
                        return;

                    if (iface == "wl_seat")
                        probe.HasSeat = true;
                    else if (iface == "org_kde_kwin_idle")
                        probe.HasKdeIdle = true;
                };

                onGlobalRemove = static (data, reg, name) => { };

                listenerTable = ListenerTableBuilder.Build(
                    Marshal.GetFunctionPointerForDelegate(onGlobal),
                    Marshal.GetFunctionPointerForDelegate(onGlobalRemove));

                if (WaylandNative.wl_proxy_add_listener(registry, listenerTable, GCHandle.ToIntPtr(stateHandle.Value)) != 0)
                    return false;

                // One roundtrip is enough for the compositor to report all globals.
                if (WaylandNative.wl_display_roundtrip(display) < 0)
                    return false;

                return state.HasSeat && state.HasKdeIdle;
            }
            catch
            {
                return false;
            }
            finally
            {
                // Clean up in the reverse order of allocation, guarding each
                // step so a partial failure doesn't leak resources.
                if (registry != IntPtr.Zero)
                    WaylandNative.wl_proxy_destroy(registry);

                if (display != IntPtr.Zero)
                    WaylandNative.wl_display_disconnect(display);

                if (listenerTable != IntPtr.Zero)
                    Marshal.FreeHGlobal(listenerTable);

                if (stateHandle is { } h && h.IsAllocated)
                    h.Free();
            }
        }, ct);
    }

    // =========================================================================
    // Nested types — native interop
    // =========================================================================

    #region Native structures and delegates

    /// <summary>
    /// POSIX <c>timespec</c>, used with <c>wl_display_dispatch_timeout</c>.
    /// Must match the 64-bit Linux layout (tv_sec and tv_nsec are both long).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long tv_sec;
        public long tv_nsec;
    }

    /// <summary>
    /// Union that holds any Wayland argument value.
    /// All fields share offset 0 because only one is meaningful per argument.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct WlArgument
    {
        [FieldOffset(0)] public int    i; // int
        [FieldOffset(0)] public uint   u; // uint
        [FieldOffset(0)] public int    f; // fixed-point
        [FieldOffset(0)] public IntPtr s; // string
        [FieldOffset(0)] public IntPtr o; // object/new_id
        [FieldOffset(0)] public uint   n; // new_id (numeric)
        [FieldOffset(0)] public IntPtr a; // array
        [FieldOffset(0)] public int    h; // fd

        public static WlArgument FromUInt(uint value)    => new() { u = value };
        public static WlArgument FromPointer(IntPtr ptr) => new() { o = ptr };
    }

    /// <summary>
    /// Mirrors the C <c>struct wl_message</c> from wayland-client.h.
    /// Describes a single request or event in a Wayland interface.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WlMessage
    {
        public IntPtr name;      // const char *
        public IntPtr signature; // const char * (type codes: u, i, o, n, s, …)
        public IntPtr types;     // const struct wl_interface **
    }

    /// <summary>
    /// Mirrors the C <c>struct wl_interface</c> from wayland-client.h.
    /// Describes a complete Wayland protocol interface (its requests and events).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WlInterface
    {
        public IntPtr name;         // const char *
        public int    version;
        public int    method_count;
        public IntPtr methods;      // const struct wl_message *
        public int    event_count;
        public IntPtr events;       // const struct wl_message *
    }

    // Delegate types matching the C function signatures the compositor calls.

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RegistryGlobalDelegate(
        IntPtr data, IntPtr registry, uint name, IntPtr interfacePtr, uint version);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RegistryGlobalRemoveDelegate(
        IntPtr data, IntPtr registry, uint name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IdleDelegate(IntPtr data, IntPtr timeoutProxy);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ResumedDelegate(IntPtr data, IntPtr timeoutProxy);

    #endregion

    // =========================================================================
    // Nested types — libwayland P/Invoke
    // =========================================================================

    #region WaylandNative (P/Invoke)

    /// <summary>
    /// P/Invoke bindings for the <c>libwayland-client</c> functions we use.
    /// </summary>
    private static class WaylandNative
    {
        /// <summary>Open the Wayland display connection (uses $WAYLAND_DISPLAY if name is null).</summary>
        [DllImport(WaylandLib, EntryPoint = "wl_display_connect", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr wl_display_connect(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

        /// <summary>Close the Wayland display connection and free all associated proxies.</summary>
        [DllImport(WaylandLib, EntryPoint = "wl_display_disconnect", CallingConvention = CallingConvention.Cdecl)]
        public static extern void wl_display_disconnect(IntPtr display);

        /// <summary>
        /// Block until the compositor has processed all pending requests and all
        /// resulting events have been dispatched. Returns the number of events
        /// dispatched, or -1 on error.
        /// </summary>
        [DllImport(WaylandLib, EntryPoint = "wl_display_roundtrip", CallingConvention = CallingConvention.Cdecl)]
        public static extern int wl_display_roundtrip(IntPtr display);

        /// <summary>Returns the last error code set on the display (0 = no error).</summary>
        [DllImport(WaylandLib, EntryPoint = "wl_display_get_error", CallingConvention = CallingConvention.Cdecl)]
        public static extern int wl_display_get_error(IntPtr display);

        /// <summary>
        /// Dispatch events, waiting at most <paramref name="timeout"/> before returning.
        /// Returns the number of events dispatched, or -1 on error.
        /// </summary>
        [DllImport(WaylandLib, EntryPoint = "wl_display_dispatch_timeout", CallingConvention = CallingConvention.Cdecl)]
        public static extern int wl_display_dispatch_timeout(IntPtr display, ref Timespec timeout);

        /// <summary>Register a listener (vtable) and opaque data pointer on a proxy.</summary>
        [DllImport(WaylandLib, EntryPoint = "wl_proxy_add_listener", CallingConvention = CallingConvention.Cdecl)]
        public static extern int wl_proxy_add_listener(IntPtr proxy, IntPtr implementation, IntPtr data);

        /// <summary>Destroy a proxy (does NOT send a protocol destructor request).</summary>
        [DllImport(WaylandLib, EntryPoint = "wl_proxy_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void wl_proxy_destroy(IntPtr proxy);

        /// <summary>
        /// Marshal a request with no arguments. When <paramref name="flags"/> includes
        /// <see cref="WlMarshalFlagDestroy"/>, also destroys the proxy (protocol destructor).
        /// </summary>
        [DllImport(WaylandLib, EntryPoint = "wl_proxy_marshal_flags", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr wl_proxy_marshal_flags(
            IntPtr proxy,
            uint   opcode,
            IntPtr @interface,
            uint   version,
            uint   flags);

        /// <summary>
        /// Marshal a request with an array of <see cref="WlArgument"/> values.
        /// Returns a new proxy if the request creates one (new_id), or IntPtr.Zero for void requests.
        /// </summary>
        [DllImport(WaylandLib, EntryPoint = "wl_proxy_marshal_array_flags", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr wl_proxy_marshal_array_flags(
            IntPtr proxy,
            uint   opcode,
            IntPtr @interface,
            uint   version,
            uint   flags,
            [In] WlArgument[] args);
    }

    #endregion

    // =========================================================================
    // Nested types — listener vtable builder
    // =========================================================================

    #region ListenerTableBuilder

    /// <summary>
    /// Allocates an unmanaged block of memory containing a packed array of
    /// function pointers, forming the vtable that libwayland uses to dispatch
    /// events to our callbacks.
    ///
    /// Caller is responsible for freeing the returned pointer with
    /// <see cref="Marshal.FreeHGlobal"/> when the listener is no longer needed.
    /// </summary>
    private static class ListenerTableBuilder
    {
        public static IntPtr Build(params IntPtr[] functionPointers)
        {
            if (functionPointers is null || functionPointers.Length == 0)
                throw new ArgumentException("At least one function pointer is required.", nameof(functionPointers));

            int   size = IntPtr.Size * functionPointers.Length;
            IntPtr mem = Marshal.AllocHGlobal(size);

            for (int i = 0; i < functionPointers.Length; i++)
                Marshal.WriteIntPtr(mem, i * IntPtr.Size, functionPointers[i]);

            return mem;
        }
    }

    #endregion

    // =========================================================================
    // Nested types — manually-constructed Wayland protocol interfaces
    // =========================================================================

    #region ProtocolInterfaces

    /// <summary>
    /// Thin wrapper so we can pass a typed pointer for a protocol interface
    /// rather than a raw <see cref="IntPtr"/>.
    /// </summary>
    private sealed class ProtocolInterface
    {
        public ProtocolInterface(IntPtr pointer) => Pointer = pointer;
        public IntPtr Pointer { get; }
    }

    /// <summary>
    /// Holds pointers to <c>struct wl_interface</c> descriptors for each
    /// protocol interface we use.
    ///
    /// libwayland exports <c>wl_registry_interface</c> and <c>wl_seat_interface</c>
    /// directly, so we load those from the shared library. The KWin-specific
    /// interfaces are not exported by libwayland and must be built by hand in
    /// unmanaged memory, mirroring the protocol XML.
    /// </summary>
    private static class ProtocolInterfaces
    {
        private static readonly IntPtr LibHandle = NativeLibrary.Load(WaylandLib);

        /// <summary>Exported by libwayland-client; describes wl_registry.</summary>
        public static readonly IntPtr WlRegistryInterface =
            NativeLibrary.GetExport(LibHandle, "wl_registry_interface");

        /// <summary>Exported by libwayland-client; describes wl_seat.</summary>
        public static readonly IntPtr WlSeatInterface =
            NativeLibrary.GetExport(LibHandle, "wl_seat_interface");

        /// <summary>Hand-built interface descriptor for org_kde_kwin_idle_timeout.</summary>
        public static readonly ProtocolInterface OrgKdeKwinIdleTimeout =
            new(CreateOrgKdeKwinIdleTimeoutInterface());

        /// <summary>Hand-built interface descriptor for org_kde_kwin_idle.</summary>
        public static readonly ProtocolInterface OrgKdeKwinIdle =
            new(CreateOrgKdeKwinIdleInterface());

        // ---------------------------------------------------------------------
        // Interface construction helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Builds a wl_interface for org_kde_kwin_idle (the idle manager global).
        ///
        /// Protocol definition:
        ///   requests:
        ///     get_idle_timeout(new_id&lt;org_kde_kwin_idle_timeout&gt;, seat, timeout_ms)  → "nou"
        ///   events: none
        /// </summary>
        private static IntPtr CreateOrgKdeKwinIdleInterface()
        {
            // The 'types' array for get_idle_timeout lists the interface pointers
            // for each argument that is an object/new_id type.
            var getIdleTimeoutTypes = AllocInterfacePointerArray(
                OrgKdeKwinIdleTimeout.Pointer, // new_id → org_kde_kwin_idle_timeout
                WlSeatInterface,               // object → wl_seat
                IntPtr.Zero);                  // uint has no interface

            var methods = AllocMessageArray(new[]
            {
                new MessageSpec("get_idle_timeout", "nou", getIdleTimeoutTypes)
                //                                   n = new_id
                //                                    o = object (seat)
                //                                     u = uint (timeout ms)
            });

            return AllocInterface("org_kde_kwin_idle", version: 1, methods, eventCount: 0, eventsPtr: IntPtr.Zero);
        }

        /// <summary>
        /// Builds a wl_interface for org_kde_kwin_idle_timeout.
        ///
        /// Protocol definition:
        ///   requests:
        ///     release()               — protocol destructor
        ///     simulate_user_activity()
        ///   events:
        ///     idle()
        ///     resumed()
        /// </summary>
        private static IntPtr CreateOrgKdeKwinIdleTimeoutInterface()
        {
            var methods = AllocMessageArray(new[]
            {
                new MessageSpec("release",                "", IntPtr.Zero),
                new MessageSpec("simulate_user_activity", "", IntPtr.Zero)
            });

            var events = AllocMessageArray(new[]
            {
                new MessageSpec("idle",    "", IntPtr.Zero),
                new MessageSpec("resumed", "", IntPtr.Zero)
            });

            return AllocInterface("org_kde_kwin_idle_timeout", version: 1, methods, eventCount: 2, eventsPtr: events.ptr);
        }

        // ---------------------------------------------------------------------
        // Low-level unmanaged allocation helpers
        // ---------------------------------------------------------------------

        private static IntPtr AllocInterface(
            string name,
            int version,
            (int count, IntPtr ptr) methods,
            int eventCount,
            IntPtr eventsPtr)
        {
            var iface = new WlInterface
            {
                name         = AllocUtf8(name),
                version      = version,
                method_count = methods.count,
                methods      = methods.ptr,
                event_count  = eventCount,
                events       = eventsPtr
            };

            IntPtr mem = Marshal.AllocHGlobal(Marshal.SizeOf<WlInterface>());
            Marshal.StructureToPtr(iface, mem, fDeleteOld: false);
            return mem;
        }

        private static (int count, IntPtr ptr) AllocMessageArray(MessageSpec[] specs)
        {
            if (specs.Length == 0)
                return (0, IntPtr.Zero);

            int    size = Marshal.SizeOf<WlMessage>() * specs.Length;
            IntPtr mem  = Marshal.AllocHGlobal(size);

            for (int i = 0; i < specs.Length; i++)
            {
                var msg = new WlMessage
                {
                    name      = AllocUtf8(specs[i].Name),
                    signature = AllocUtf8(specs[i].Signature),
                    types     = specs[i].Types
                };

                IntPtr itemPtr = mem + (i * Marshal.SizeOf<WlMessage>());
                Marshal.StructureToPtr(msg, itemPtr, fDeleteOld: false);
            }

            return (specs.Length, mem);
        }

        /// <summary>
        /// Allocates an array of interface pointers (const struct wl_interface **)
        /// used in the 'types' field of a wl_message.
        /// </summary>
        private static IntPtr AllocInterfacePointerArray(params IntPtr[] pointers)
        {
            IntPtr mem = Marshal.AllocHGlobal(IntPtr.Size * pointers.Length);

            for (int i = 0; i < pointers.Length; i++)
                Marshal.WriteIntPtr(mem, i * IntPtr.Size, pointers[i]);

            return mem;
        }

        /// <summary>
        /// Copies <paramref name="value"/> as a null-terminated UTF-8 string
        /// into unmanaged heap memory and returns the pointer.
        /// </summary>
        private static IntPtr AllocUtf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value + '\0');
            IntPtr mem   = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            return mem;
        }

        private readonly record struct MessageSpec(string Name, string Signature, IntPtr Types);
    }

    #endregion

    // =========================================================================
    // Nested types — availability probe state
    // =========================================================================

    /// <summary>
    /// Simple mutable state bag passed as the opaque data pointer to the
    /// probe registry callbacks in <see cref="DetermineAvailability"/>.
    /// </summary>
    private sealed class AvailabilityProbeState
    {
        public bool HasSeat;
        public bool HasKdeIdle;
    }
}
