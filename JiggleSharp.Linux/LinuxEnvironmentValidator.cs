using System.Diagnostics;
using JiggleSharp.Core.Hosting;

namespace JiggleSharp.Linux;

public class LinuxEnvironmentValidator : IEnvironmentValidator
{
    /// <summary>
    /// Returns whether Ydotoold service is running and if the path to the Ydotoold proxy
    /// can be properly parsed from the service definition
    /// </summary>
    /// <returns></returns>
    public bool VerifyDependencies()
    {
        return SystemctlProxy.YdotoolIsRunning(out _) && SystemctlProxy.TryGetYtooldProxyPath(out _);
    }
}