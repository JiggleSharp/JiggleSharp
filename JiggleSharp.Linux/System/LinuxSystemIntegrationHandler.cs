using JiggleSharp.Core.Hosting;
using Serilog;

namespace JiggleSharp.Linux.System;

public class LinuxSystemIntegrationHandler : ISystemIntegrationHandler
{
    private static string DesktopPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart", "jigglesharp.desktop");
    
    public void HideWindowIndicator()
    {
        // Do nothing here.
    }

    public void ShowWindowIndicator()
    {
        // Do nothing here.
    }

    public bool RegisterStartupApplication()
    {
        try
        {
            if (File.Exists(DesktopPath)) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(DesktopPath)!);
            var desktop = $"""
                           [Desktop Entry]
                           Type=Application
                           Name=JiggleSharp
                           Exec={Environment.ProcessPath}
                           Hidden=false
                           NoDisplay=false
                           X-GNOME-Autostart-enabled=true
                           """;
            File.WriteAllText(DesktopPath, desktop);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LinuxSystemIntegrationHandler] Failed to register startup application");
            return false;
        }
    }

    public bool DeregisterStartupApplication()
    {
        try
        {
            if (!File.Exists(DesktopPath)) return true;
            
            File.Delete(DesktopPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LinuxSystemIntegrationHandler] Failed to deregister startup application");
            return false;
        }
    }
    
    public bool IsStartupApplicationRegistered()
    {
        return File.Exists(DesktopPath);
    }
}