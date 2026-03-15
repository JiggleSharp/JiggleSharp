using JiggleSharp.Core.Hosting;
using System.Runtime.InteropServices;
using Serilog;

namespace JiggleSharp.Mac.System;

/// <summary>
/// macOS implementation of <see cref="ISystemIntegrationHandler"/> providing two
/// distinct capabilities:
/// <list type="bullet">
///   <item><description>
///     <b>Dock icon visibility</b> — controls whether JiggleSharp appears in the
///     Dock and App Switcher via the NSApplication activation policy. JiggleSharp
///     runs as a menu bar utility with no persistent Dock presence; the icon is
///     shown temporarily while a window is open and hidden when all windows close.
///   </description></item>
///   <item><description>
///     <b>Login item registration</b> — registers or deregisters the app as a
///     login item using <c>SMAppService</c> (macOS 13+), which integrates with
///     System Settings → General → Login Items and correctly attributes the entry
///     to the app rather than the signing certificate.
///   </description></item>
/// </list>
/// <para>
/// All AppKit and ServiceManagement calls are made via P/Invoke into the
/// Objective-C runtime (<c>libobjc.A.dylib</c>) since these frameworks have no
/// managed bindings in a plain .NET/Avalonia project.
/// </para>
/// </summary>
public class MacSystemIntegrationHandler : ISystemIntegrationHandler
{
    // =========================================================================
    // ISystemIntegrationHandler — Dock icon
    // =========================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// Switches to the <c>Accessory</c> activation policy, hiding the Dock icon
    /// and App Switcher entry. The process remains active and can display windows.
    /// </remarks>
    public void HideWindowIndicator() => HideDockIcon();

    /// <inheritdoc/>
    /// <remarks>
    /// Switches to the <c>Regular</c> activation policy, restoring the Dock icon
    /// and App Switcher entry.
    /// </remarks>
    public void ShowWindowIndicator() => ShowDockIcon();

    // =========================================================================
    // ISystemIntegrationHandler — Login item (SMAppService)
    // =========================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <c>[SMAppService.mainAppService registerAndReturnError:nil]</c>.
    /// On first call macOS may return <c>RequiresApproval</c> rather than
    /// immediately enabling the item; the user must approve it in System Settings.
    /// Requires macOS 13 Ventura or later.
    /// </remarks>
    public bool RegisterStartupApplication()
    {
        try
        {
            var service = GetMainAppService();
            var sel = sel_registerName("registerAndReturnError:");
            return objc_msgSend_bool(service, sel, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MacSystemIntegrationHandler] Failed to register startup application");
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <c>[SMAppService.mainAppService unregisterAndReturnError:nil]</c>.
    /// Requires macOS 13 Ventura or later.
    /// </remarks>
    public bool DeregisterStartupApplication()
    {
        try
        {
            var service = GetMainAppService();
            var sel = sel_registerName("unregisterAndReturnError:");
            return objc_msgSend_bool(service, sel, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MacSystemIntegrationHandler] Failed to deregister startup application");
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Queries <c>[SMAppService.mainAppService status]</c> and returns
    /// <see langword="true"/> only when the status is <c>Enabled</c>.
    /// A status of <c>RequiresApproval</c> is logged as a warning and treated as
    /// <see langword="false"/> — the checkbox should remain unchecked until the
    /// user approves the item in System Settings → General → Login Items.
    /// Requires macOS 13 Ventura or later.
    /// </remarks>
    public bool IsStartupApplicationRegistered()
    {
        try
        {
            var service = GetMainAppService();
            var sel = sel_registerName("status");
            var status = (SMAppServiceStatus)objc_msgSend_int(service, sel);

            if (status == SMAppServiceStatus.RequiresApproval)
                Log.Warning("[MacSystemIntegrationHandler] Login item requires user approval " +
                            "in System Settings → General → Login Items");

            return status == SMAppServiceStatus.Enabled;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MacSystemIntegrationHandler] Failed to query startup registration status");
            return false;
        }
    }

    // =========================================================================
    // Public static helpers — Dock icon
    // =========================================================================

    /// <summary>
    /// Shows the Dock icon by switching NSApplication to the
    /// <c>Regular</c> activation policy.
    /// </summary>
    public static void ShowDockIcon() => SetPolicy(NSApplicationActivationPolicy.Regular);

    /// <summary>
    /// Hides the Dock icon by switching NSApplication to the
    /// <c>Accessory</c> activation policy. The process stays active and
    /// windows remain functional.
    /// </summary>
    public static void HideDockIcon() => SetPolicy(NSApplicationActivationPolicy.Accessory);

    // =========================================================================
    // Private — SMAppService helpers
    // =========================================================================

    /// <summary>
    /// Returns the shared <c>SMAppService</c> instance for the main app bundle.
    /// Equivalent to <c>[SMAppService mainAppService]</c>.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the ServiceManagement framework cannot be loaded, which
    /// indicates the OS is older than macOS 13.
    /// </exception>
    private static IntPtr GetMainAppService()
    {
        // Force-load ServiceManagement before asking for the class.
        NativeLibrary.TryLoad(ServiceManagementLib, out _);

        var cls = objc_getClass("SMAppService");
        if (cls == IntPtr.Zero)
            throw new PlatformNotSupportedException(
                "SMAppService is not available — requires macOS 13 Ventura or later.");

        var sel = sel_registerName("mainAppService");
        return objc_msgSend(cls, sel);
    }

    // =========================================================================
    // Private — NSApplication activation policy helper
    // =========================================================================

    /// <summary>
    /// Applies the given activation policy to the shared NSApplication instance.
    /// Changes are reflected in the Dock and App Switcher immediately.
    /// </summary>
    private static void SetPolicy(NSApplicationActivationPolicy policy)
    {
        var cls = objc_getClass("NSApplication");
        var app = objc_msgSend_IntPtr(cls, sel_registerName("sharedApplication"));
        objc_msgSend_bool_long(app, sel_registerName("setActivationPolicy:"), (long)policy);
    }

    // =========================================================================
    // Private enums
    // =========================================================================

    /// <summary>
    /// Maps to <c>NSApplicationActivationPolicy</c> in AppKit.
    /// </summary>
    private enum NSApplicationActivationPolicy : long
    {
        /// <summary>Normal foreground app — Dock icon and App Switcher entry visible.</summary>
        Regular = 0,

        /// <summary>Accessory app — no Dock icon or App Switcher entry, but windows
        /// and menu bar items work normally.</summary>
        Accessory = 1,

        /// <summary>Background-only app — no UI elements of any kind.</summary>
        Prohibited = 2
    }

    /// <summary>
    /// Maps to <c>SMAppServiceStatus</c> in the ServiceManagement framework.
    /// </summary>
    private enum SMAppServiceStatus
    {
        /// <summary>The item has not been registered.</summary>
        NotRegistered = 0,

        /// <summary>The item is registered and will launch at login.</summary>
        Enabled = 1,

        /// <summary>The item is registered but awaiting user approval in System Settings.</summary>
        RequiresApproval = 2,

        /// <summary>The item could not be found (bundle ID mismatch or missing bundle).</summary>
        NotFound = 3
    }

    // =========================================================================
    // P/Invoke — Objective-C runtime (libobjc.A.dylib)
    // =========================================================================

    private const string ObjCLib             = "/usr/lib/libobjc.A.dylib";
    private const string ServiceManagementLib = "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";

    /// <summary>Returns the class object for the named Objective-C class, or
    /// <see cref="IntPtr.Zero"/> if the class does not exist.</summary>
    [DllImport(ObjCLib)]
    private static extern IntPtr objc_getClass(string name);

    /// <summary>Registers a selector by name and returns its opaque handle.</summary>
    [DllImport(ObjCLib)]
    private static extern IntPtr sel_registerName(string name);

    /// <summary>Sends a zero-argument message returning an <see cref="IntPtr"/>.
    /// Used for <c>sharedApplication</c> and <c>mainAppService</c>.</summary>
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    /// <summary>Sends a zero-argument message returning an <see cref="IntPtr"/>.
    /// Used for class-level factory messages such as <c>mainAppService</c>.</summary>
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    /// <summary>Sends a zero-argument message returning an <c>int</c>.
    /// Used to read <c>SMAppServiceStatus</c> via the <c>status</c> property.</summary>
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector);

    /// <summary>Sends a two-pointer-argument message returning a <c>bool</c>.
    /// Used for <c>registerAndReturnError:</c> and <c>unregisterAndReturnError:</c>
    /// with <see cref="IntPtr.Zero"/> for the unused <c>NSError**</c> parameter.</summary>
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector,
                                                  IntPtr arg1, IntPtr arg2);

    /// <summary>Sends a single-<c>long</c>-argument message returning a <c>bool</c>.
    /// Used for <c>setActivationPolicy:</c>.</summary>
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern bool objc_msgSend_bool_long(IntPtr receiver, IntPtr selector, long arg);
}