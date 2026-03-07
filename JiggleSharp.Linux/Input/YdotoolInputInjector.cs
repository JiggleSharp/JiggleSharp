using System.Diagnostics;
using JiggleSharp.Core.Input;
using Serilog;

namespace JiggleSharp.Linux.Input;

public class YdotoolInputInjector : IInputInjector
{
    private const string _ydotoolsocket = "/tmp/.ydotool_socket";
    
    public YdotoolInputInjector()
    {
        if (!SystemctlProxy.TryGetYtooldProxyPath(out var _ydotoolsocket))
        {
            Log.Error($"ydotoold service was not found. Please make sure it is installed and running.");
        }
        else
        {
            Log.Information($"ydotoold service found at {_ydotoolsocket}");
        }
    }
    
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
                    ["YDOTOOL_SOCKET"] = _ydotoolsocket
                }
            };
            proc.Start();
            proc.WaitForExit();
        }
        catch { /* fire and forget */ }

        return Task.CompletedTask;
    }
}