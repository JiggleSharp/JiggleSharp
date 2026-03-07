using System.Diagnostics;
using JiggleSharp.Core.Input;

namespace JiggleSharp.Linux.Input;

public class YdotoolInputInjector : IInputInjector
{
    public Task MoveMouseAsync(int dx, int dy, CancellationToken ct)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new("ydotool", $"mousemove -- {dx} {dy}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                EnvironmentVariables =
                {
                    ["YDOTOOL_SOCKET"] = "/tmp/.ydotool_socket"
                }
            };
            proc.Start();
            proc.WaitForExit();
        }
        catch { /* fire and forget */ }

        return Task.CompletedTask;
    }
}