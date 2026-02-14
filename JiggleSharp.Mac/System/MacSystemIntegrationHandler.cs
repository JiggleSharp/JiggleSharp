using JiggleSharp.Core.Hosting;
using System;
using System.Runtime.InteropServices;

namespace JiggleSharp.Mac.System;

public class MacSystemIntegrationHandler : ISystemIntegrationHandler
{
    public void HideWindowIndicator()
    {
        HideDockIcon();
    }

    public void ShowWindowIndicator()
    {
        ShowDockIcon();
    }
    
    private enum NSApplicationActivationPolicy : long
    {
        Regular = 0,    // shows Dock icon
        Accessory = 1,  // no Dock icon, can still show windows
        Prohibited = 2
    }

    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    // sharedApplication
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    // setActivationPolicy:
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern bool objc_msgSend_bool_IntPtr_long(IntPtr receiver, IntPtr selector, long arg1);

    public static void ShowDockIcon() => SetPolicy(NSApplicationActivationPolicy.Regular);
    public static void HideDockIcon() => SetPolicy(NSApplicationActivationPolicy.Accessory);

    private static void SetPolicy(NSApplicationActivationPolicy policy)
    {
        var nsAppClass = objc_getClass("NSApplication");
        var sharedSel = sel_registerName("sharedApplication");
        var app = objc_msgSend_IntPtr(nsAppClass, sharedSel);

        var setPolicySel = sel_registerName("setActivationPolicy:");
        _ = objc_msgSend_bool_IntPtr_long(app, setPolicySel, (long)policy);
    }
}