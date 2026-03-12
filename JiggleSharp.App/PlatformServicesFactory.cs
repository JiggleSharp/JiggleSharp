using System;
using JiggleSharp.Core.Engine;
using JiggleSharp.Linux;
using JiggleSharp.Mac;
using JiggleSharp.Windows;

namespace JiggleSharp.App;

public static class PlatformServicesFactory
{
    public static IPlatformServices Create()
    {
        if (OperatingSystem.IsLinux())
            return new LinuxPlatformServices();

        if (OperatingSystem.IsWindows())
            return new WindowsPlatformServices();

        return OperatingSystem.IsMacOS() ? new MacPlatformServices() : throw new PlatformNotSupportedException();
    }
}