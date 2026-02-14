using System;
using JiggleSharp.Core.Engine;
using JiggleSharp.Linux;
using JiggleSharp.Mac;

namespace JiggleSharp.App;

public static class PlatformServicesFactory
{
    public static IPlatformServices Create()
    {
        if (OperatingSystem.IsLinux())
            return new LinuxPlatformServices();

        //if (OperatingSystem.IsWindows())
        //    return new WindowsPlatformServices();

        if (OperatingSystem.IsMacOS())
            return new MacPlatformServices();

        throw new PlatformNotSupportedException();
    }
}