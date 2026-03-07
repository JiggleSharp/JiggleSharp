using System.Diagnostics;
using JiggleSharp.Core.Hosting;
using Serilog;

namespace JiggleSharp.Linux;

/// <summary>
/// Validates that the Linux-specific runtime dependencies required by
/// JiggleSharp are satisfied on the current machine.
///
/// Currently checks:
///   - The <c>ydotoold</c> daemon is running as a systemd service.
///   - The socket/proxy path for <c>ydotoold</c> can be parsed from its
///     service definition (required to open the ydotool connection).
/// </summary>
public class LinuxEnvironmentValidator : IEnvironmentValidator
{
    /// <summary>
    /// Checks that all Linux runtime dependencies are present and operational.
    ///
    /// Specifically, verifies via <see cref="SystemctlProxy"/> that:
    ///   1. The <c>ydotoold</c> systemd service is currently running.
    ///   2. The <c>ydotoold</c> proxy socket path can be successfully parsed
    ///      from the service definition.
    ///
    /// Both conditions must be true for input simulation to function. If
    /// either fails, the application should surface an actionable error to
    /// the user (e.g. "run: systemctl --user enable --now ydotoold").
    /// </summary>
    /// <returns>
    /// <c>true</c> if <c>ydotoold</c> is running and its proxy path is
    /// resolvable; <c>false</c> otherwise.
    /// </returns>
    public bool VerifyDependencies()
    {
        var ydotoolIsRunning = SystemctlProxy.YdotoolIsRunning(out var error);
        var ydotoolProxyPath = SystemctlProxy.TryGetYtooldProxyPath(out var proxyPath);
        
        if (!ydotoolIsRunning)
            Log.Error("ydotoold.service is not running or was not found. Verify the service is installed and running.");
        
        if (!ydotoolProxyPath)
            Log.Error("Failed to parse ydotoold proxy path from service definition.");

        return ydotoolIsRunning
               && ydotoolProxyPath;
    }
}