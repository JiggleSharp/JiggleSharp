using JiggleSharp.Core.Hosting;
using System.Runtime.InteropServices;
using Serilog;

namespace JiggleSharp.Mac.System;

/// <summary>
/// macOS implementation of <see cref="ISystemIntegrationHandler"/> that controls
/// the application's Dock icon visibility via the NSApplication activation policy.
///
/// JiggleSharp runs as a tray-only app with no persistent Dock presence. This
/// handler allows the Dock icon to be shown temporarily when a window is open
/// and hidden again when all windows are closed, matching standard macOS
/// menu bar utility conventions.
/// </summary>
public class MacSystemIntegrationHandler : ISystemIntegrationHandler
{
    private static string PlistPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", "com.jigglesharp.jigglesharp.plist");
    
    // =========================================================================
    // ISystemIntegrationHandler
    // =========================================================================

    /// <summary>
    /// Hides the Dock icon by switching to the <c>Accessory</c> activation
    /// policy. The app remains functional and can still show windows.
    /// </summary>
    public void HideWindowIndicator() => HideDockIcon();

    /// <summary>
    /// Shows the Dock icon by switching to the <c>Regular</c> activation policy.
    /// </summary>
    public void ShowWindowIndicator() => ShowDockIcon();

    public bool RegisterStartupApplication()
    {
        try
        {
            if (File.Exists(PlistPath)) return true;
            var plist = $"""
                         <?xml version="1.0" encoding="UTF-8"?>
                         <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
                             "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                         <plist version="1.0">
                         <dict>
                             <key>Label</key>
                             <string>com.jigglesharp.jigglesharp</string>
                             <key>ProgramArguments</key>
                             <array>
                                 <string>{Environment.ProcessPath}</string>
                             </array>
                             <key>RunAtLoad</key>
                             <true/>
                             <key>AssociatedBundleIdentifiers</key>
                             <array>
                                 <string>com.jigglesharp.jigglesharp</string>
                             </array>
                         </dict>
                         </plist>
                         """;
            File.WriteAllText(PlistPath, plist);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MacSystemIntegrationHandler] Failed to register startup application");
            return false;
        }
    }

    public bool DeregisterStartupApplication()
    {
        try
        {
            if (!File.Exists(PlistPath)) return true;
            
            File.Delete(PlistPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MacSystemIntegrationHandler] Failed to deregister startup application");
            return false;
        }
    }

    // =========================================================================
    // Public helpers
    // =========================================================================

    /// <summary>Shows the Dock icon.</summary>
    public static void ShowDockIcon() => SetPolicy(NSApplicationActivationPolicy.Regular);

    /// <summary>Hides the Dock icon. The app remains active and can show windows.</summary>
    public static void HideDockIcon() => SetPolicy(NSApplicationActivationPolicy.Accessory);

    // =========================================================================
    // NSApplicationActivationPolicy
    // =========================================================================

    /// <summary>
    /// Controls how the application appears in the Dock and App Switcher.
    /// Maps directly to <c>NSApplicationActivationPolicy</c> in AppKit.
    /// </summary>
    private enum NSApplicationActivationPolicy : long
    {
        /// <summary>Normal app — appears in Dock and App Switcher.</summary>
        Regular = 0,

        /// <summary>Accessory app — no Dock icon, no App Switcher entry,
        /// but can still display windows and a menu bar icon.</summary>
        Accessory = 1,

        /// <summary>Prohibited — no UI whatsoever.</summary>
        Prohibited = 2
    }

    // =========================================================================
    // P/Invoke — Objective-C runtime
    // =========================================================================

    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>Returns the class object for the named Objective-C class.</summary>
    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    /// <summary>Registers a method selector by name and returns its handle.</summary>
    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    /// <summary>Sends a message to an Objective-C object, returning an <see cref="IntPtr"/>.
    /// Used here to call <c>[NSApplication sharedApplication]</c>.</summary>
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    /// <summary>Sends a message to an Objective-C object with a single <c>long</c> argument,
    /// returning a <c>bool</c>. Used here to call <c>setActivationPolicy:</c>.</summary>
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern bool objc_msgSend_bool_long(IntPtr receiver, IntPtr selector, long arg);

    // =========================================================================
    // Implementation
    // =========================================================================

    /// <summary>
    /// Sets the NSApplication activation policy, which controls Dock icon
    /// visibility. Changes take effect immediately.
    /// </summary>
    private static void SetPolicy(NSApplicationActivationPolicy policy)
    {
        var nsAppClass = objc_getClass("NSApplication");
        var sharedSel  = sel_registerName("sharedApplication");
        var app        = objc_msgSend_IntPtr(nsAppClass, sharedSel);

        var setPolicySel = sel_registerName("setActivationPolicy:");
        objc_msgSend_bool_long(app, setPolicySel, (long)policy);
    }
}