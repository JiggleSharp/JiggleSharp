using System.Diagnostics;
using JiggleSharp.Core.Input;
using Serilog;

namespace JiggleSharp.Linux.Input;

public class YdotoolInputInjector : IInputInjector
{
    public event EventHandler<Exception>? InputInjectorFailure;
    
    private const string _ydotoolsocket = "/tmp/.ydotool_socket";
    
    public YdotoolInputInjector()
    {
        if (!SystemctlProxy.TryGetYtooldProxyPath(out var _ydotoolsocket))
        {
            var error = new InputInjectorException("ydotoold service was not found. Please make sure it is " +
                                                   "installed and running.");
            Log.Error(error.Message);
            InputInjectorFailure?.Invoke(this, error);
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
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                EnvironmentVariables =
                {
                    ["YDOTOOL_SOCKET"] = _ydotoolsocket
                }
            };
            proc.Start();
            proc.WaitForExit();
        }
        catch (Exception ex)
        {
            var error = new InputInjectorException($"An error occurred while moving the mouse: {ex.Message}", ex);
            Log.Error(error.Message);
            InputInjectorFailure?.Invoke(this, error);
            return Task.FromException(ex);
        }

        return Task.CompletedTask;
    }
}