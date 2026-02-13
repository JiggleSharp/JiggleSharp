using System.Diagnostics;

namespace JiggleSharp.Linux;

internal static class YtooldProxy
{
    /// <summary>
    /// Verifies that ydotoold service is running and the user is able to access it
    /// </summary>
    /// <param name="error">Errors returned when validating that ydotool is running</param>
    /// <returns></returns>
    public static bool YdotoolIsRunning(out string? error)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "systemctl",
                ArgumentList = { "is-active", "ydotoold.service" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

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

            error = $"ydotoold not active (state: {output})";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
    
    /// <summary>
    /// Gets the ytoold service proxy path
    /// </summary>
    /// <returns></returns>
    public static bool TryGetYtooldProxyPath(out string? path)
    {
        path = null;
        
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "systemctl",
                ArgumentList =
                {
                    "show",
                    "ydotoold.service",
                    "--property=ExecStart",
                    "--value"
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process == null)
                return false;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return false;

            // Split arguments
            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var socketArg = parts.FirstOrDefault(p => p.StartsWith("--socket-path="));

            path = socketArg?.Substring("--socket-path=".Length);
        }
        catch
        {
            return false;
        }

        return true;
    }
}