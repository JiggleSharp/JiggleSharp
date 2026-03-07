using System.Diagnostics;

namespace JiggleSharp.Linux;

/// <summary>
/// Thin wrapper around <c>systemctl</c> for querying the state of the
/// <c>ydotoold</c> systemd service.
///
/// All methods spawn a short-lived <c>systemctl</c> process and are
/// therefore synchronous and blocking. They are intended to be called
/// once at startup (via <see cref="LinuxEnvironmentValidator"/>) rather
/// than on a hot path.
/// </summary>
internal static class SystemctlProxy
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const string ServiceName  = "ydotoold.service";
    private const string SocketArgPrefix = "--socket-path=";

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Returns <c>true</c> if the <c>ydotoold</c> systemd service is currently
    /// active (i.e. <c>systemctl is-active ydotoold.service</c> exits 0 and
    /// prints <c>active</c>).
    /// </summary>
    /// <param name="error">
    /// Human-readable reason for failure, or <c>null</c> on success. Suitable
    /// for surfacing directly to the user (e.g. in a setup dialog).
    /// </param>
    public static bool YdotoolIsRunning(out string? error)
    {
        try
        {
            using var process = StartSystemctl("is-active", ServiceName);

            if (process == null)
            {
                error = "Failed to start systemctl process.";
                return false;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && output == "active")
            {
                error = null;
                return true;
            }

            error = $"ydotoold is not active (systemctl state: '{output}').";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Attempts to parse the <c>--socket-path</c> argument from the
    /// <c>ydotoold</c> service's <c>ExecStart</c> line by running
    /// <c>systemctl show ydotoold.service --property=ExecStart --value</c>.
    ///
    /// The socket path is required to open the ydotool connection for input
    /// injection.
    /// </summary>
    /// <param name="path">
    /// The resolved socket path (e.g. <c>/tmp/.ydotoold</c>), or <c>null</c>
    /// if the argument could not be found or the command failed.
    /// </param>
    /// <returns>
    /// <c>true</c> if the <c>--socket-path</c> argument was successfully
    /// parsed from the service definition; <c>false</c> otherwise.
    /// </returns>
    public static bool TryGetYtooldProxyPath(out string? path)
    {
        path = null;

        try
        {
            using var process = StartSystemctl(
                "show", ServiceName, "--property=ExecStart", "--value");

            if (process == null)
                return false;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return false;

            // ExecStart is returned as a space-separated argument list.
            // Find the --socket-path=<value> token and strip the prefix.
            var parts     = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var socketArg = parts.FirstOrDefault(p => p.StartsWith(SocketArgPrefix));
            path          = socketArg?[SocketArgPrefix.Length..];
        }
        catch
        {
            return false;
        }

        return path != null;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Starts a <c>systemctl</c> process with the supplied arguments and
    /// stdout/stderr redirected. Returns <c>null</c> if the process could
    /// not be started.
    /// </summary>
    private static Process? StartSystemctl(params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName        = "systemctl",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false
        };

        foreach (var arg in arguments)
            info.ArgumentList.Add(arg);

        return Process.Start(info);
    }
}