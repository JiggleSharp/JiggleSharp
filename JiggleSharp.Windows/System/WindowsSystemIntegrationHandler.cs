using Microsoft.Win32;
using JiggleSharp.Core.Hosting;
using Serilog;

namespace JiggleSharp.Windows.System;

/// <summary>
/// Windows implementation of <see cref="ISystemIntegrationHandler"/>.
/// No-op on Windows — taskbar presence is tied to window creation and requires
/// no explicit management. See the macOS implementation for context.
/// </summary>
public class WindowsSystemIntegrationHandler : ISystemIntegrationHandler
{
    private const string StartupRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "JiggleSharp";
    
    /// <inheritdoc/>
    public void ShowWindowIndicator() { }

    /// <inheritdoc/>
    public void HideWindowIndicator() { }

    public bool RegisterStartupApplication()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    StartupRegistryKeyPath, writable: true);
                key!.SetValue(AppName, Environment.ProcessPath!);
                return true;
            }
            else
                throw new PlatformNotSupportedException("The Windows Registry is not supported on this platform.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WindowsSystemIntegrationHandler] Failed to register startup application");
            return false;
        }
    }

    public bool DeregisterStartupApplication()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    StartupRegistryKeyPath, writable: true);
                key!.DeleteValue(AppName, false);
                return true;
            }
            else
                throw new PlatformNotSupportedException("The Windows Registry is not supported on this platform.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WindowsSystemIntegrationHandler] Failed to register startup application");
            return false;
        }
    }
}