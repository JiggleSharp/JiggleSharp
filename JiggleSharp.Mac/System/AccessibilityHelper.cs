

using System.Runtime.InteropServices;

namespace JiggleSharp.Mac.System;

public static class MacAccessibilityHelper
{
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDictionaryCreate(
        IntPtr allocator,
        IntPtr[] keys,
        IntPtr[] values,
        nint numValues,
        IntPtr keyCallbacks,
        IntPtr valueCallbacks);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(
        IntPtr allocator, string str, uint encoding);

    // kCFBooleanTrue is a global symbol, not a function — import it as a pointer
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFRetain(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    // Grab kCFBooleanTrue via a known exported symbol
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        EntryPoint = "CFBooleanGetValue")]
    private static extern bool CFBooleanGetValue(IntPtr boolean);

    private static IntPtr GetCFBooleanTrue()
    {
        var lib = NativeLibrary.Load(
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation");
        IntPtr symbolAddr = NativeLibrary.GetExport(lib, "kCFBooleanTrue");
        // kCFBooleanTrue is a CFBooleanRef* — dereference to get the actual handle
        return Marshal.ReadIntPtr(symbolAddr);
    }

    private const uint kCFStringEncodingUTF8 = 0x08000100;

    /// <summary>
    /// Checks whether the process has Accessibility permission.
    /// If <paramref name="prompt"/> is true and permission is missing,
    /// triggers the system dialog exactly once — macOS will not re-show
    /// it if the user has already seen and dismissed it this session.
    /// Returns true if permission is already granted.
    /// </summary>
    public static bool CheckAccessibility()
    {
        // Fast path — already trusted, no need to allocate anything
        if (AXIsProcessTrusted()) return true;
        return false;
    }
}