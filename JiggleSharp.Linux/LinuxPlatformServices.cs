using JiggleSharp.Core.Engine;
using JiggleSharp.Core.Hosting;
using JiggleSharp.Core.Idle;
using JiggleSharp.Core.Input;
using JiggleSharp.Linux.Idle.KWin;
using JiggleSharp.Linux.Idle.Mutter;
using JiggleSharp.Linux.Input;
using JiggleSharp.Linux.Logging;
using JiggleSharp.Linux.System;

namespace JiggleSharp.Linux;

/// <summary>
/// Linux implementation of <see cref="IPlatformServices"/>. Wires up the
/// concrete service implementations for input injection, environment
/// validation, system integration, and — where supported — idle time
/// monitoring.
///
/// Idle provider selection:
///   The idle provider is chosen at construction time based on the current
///   Wayland compositor. The environment variables <c>XDG_SESSION_TYPE</c>
///   and <c>XDG_CURRENT_DESKTOP</c> are read, and short-lived probe instances
///   of each provider are used to test availability:
///
///   - Wayland + GNOME / Mutter → <see cref="MutterIdleTimeProvider"/>
///   - Wayland + KDE / KWin    → <see cref="KWinIdleTimeProvider"/>
///   - X11 or unrecognised     → <c>null</c> (idle monitoring disabled)
///
///   Mutter is probed first because GNOME is the more common target desktop.
///   If neither probe succeeds, <see cref="IdleTimeProvider"/> is left null
///   and the engine should disable idle-based behaviour gracefully.
/// </summary>
public class LinuxPlatformServices : IPlatformServices
{
    // =========================================================================
    // IPlatformServices — properties
    // =========================================================================

    /// <summary>
    /// Idle time provider selected for the current compositor, or <c>null</c>
    /// if idle monitoring is not supported in this session.
    /// </summary>
    public IIdleTimeProvider? IdleTimeProvider { get; }

    /// <summary>Injects input events via <c>ydotoold</c>.</summary>
    public IInputInjector InputInjector { get; } = new YdotoolInputInjector();

    /// <summary>Validates that <c>ydotoold</c> is running and reachable.</summary>
    public IEnvironmentValidator EnvironmentValidator { get; } = new LinuxEnvironmentValidator();

    /// <summary>Handles Linux-specific system integration (autostart, tray, etc.).</summary>
    public ISystemIntegrationHandler SystemIntegrationHandler { get; } = new LinuxSystemIntegrationHandler();

    /// <summary>Handles Linux-specific logging.</summary>
    public ILogger SystemLog { get; } = new LinuxSystemLog();

    // =========================================================================
    // Construction — idle provider selection
    // =========================================================================

    /// <summary>
    /// Initialises platform services and selects an idle time provider for the
    /// current session. Provider selection blocks briefly on the calling thread
    /// because <see cref="IPlatformServices"/> construction is synchronous;
    /// the probes themselves are lightweight single-roundtrip operations.
    /// </summary>
    public LinuxPlatformServices()
    {
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");

        // Idle monitoring via these providers requires a Wayland session.
        // X11 sessions are not currently supported and fall through to null.
        if (!string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
            return;

        // Block on async probes; construction must be synchronous.
        bool mutterAvailable = Task.Run(IsMutterAvailableAsync).GetAwaiter().GetResult();
        bool kwinAvailable   = Task.Run(IsKWinAvailableAsync).GetAwaiter().GetResult();

        if (mutterAvailable)
            IdleTimeProvider = new MutterIdleTimeProvider();
        else if (kwinAvailable)
            IdleTimeProvider = new KWinIdleTimeProvider();
    }

    // =========================================================================
    // Private — compositor availability probes
    // =========================================================================

    /// <summary>
    /// Probes whether the Mutter idle monitor D-Bus service is reachable.
    /// A short-lived <see cref="MutterIdleTimeProvider"/> is used so that
    /// the probe cleans up its D-Bus connection immediately after the check.
    /// </summary>
    private static async Task<bool> IsMutterAvailableAsync()
    {
        using var provider = new MutterIdleTimeProvider();
        return await provider.IsAvailableAsync();
    }

    /// <summary>
    /// Probes whether the KWin idle protocol is available on the current
    /// Wayland compositor. A short-lived <see cref="KWinIdleTimeProvider"/>
    /// is used so the Wayland connection is closed immediately after the check.
    /// </summary>
    private static async Task<bool> IsKWinAvailableAsync()
    {
        using var provider = new KWinIdleTimeProvider();
        return await provider.IsAvailableAsync();
    }
}