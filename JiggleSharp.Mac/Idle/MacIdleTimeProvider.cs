using System.Runtime.InteropServices;
using JiggleSharp.Core.Idle;

namespace JiggleSharp.Mac.Idle;

public class MacIdleTimeProvider : IIdleTimeProvider
{
    public Task<TimeSpan> GetIdleTimeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idleNs = GetHidIdleTimeNanoseconds();
        return Task.FromResult(TimeSpan.FromSeconds(idleNs / 1_000_000_000.0));
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    private static ulong GetHidIdleTimeNanoseconds()
    {
        // Get IOHIDSystem entry in IORegistry
        IntPtr matching = IOServiceMatching("IOHIDSystem");
        if (matching == IntPtr.Zero)
            throw new InvalidOperationException("IOServiceMatching(IOHIDSystem) failed.");

        uint service = IOServiceGetMatchingService(kIOMasterPortDefault, matching);
        if (service == 0)
            throw new InvalidOperationException("IOServiceGetMatchingService returned 0 (not found).");

        try
        {
            IntPtr key = CFStringCreateWithCString(IntPtr.Zero, "HIDIdleTime", kCFStringEncodingUTF8);
            if (key == IntPtr.Zero)
                throw new InvalidOperationException("CFStringCreateWithCString failed.");

            try
            {
                IntPtr value = IORegistryEntryCreateCFProperty(service, key, IntPtr.Zero, 0);
                if (value == IntPtr.Zero)
                    throw new InvalidOperationException("IORegistryEntryCreateCFProperty(HIDIdleTime) returned null.");

                try
                {
                    // HIDIdleTime is typically a CFNumber holding uint64 nanoseconds.
                    if (!CFNumberGetValue(value, kCFNumberSInt64Type, out long nsSigned))
                        throw new InvalidOperationException("CFNumberGetValue failed.");

                    return nsSigned < 0 ? 0UL : (ulong)nsSigned;
                }
                finally
                {
                    CFRelease(value);
                }
            }
            finally
            {
                CFRelease(key);
            }
        }
        finally
        {
            IOObjectRelease(service);
        }
    }
    
    // ---- IOKit ----
    private const uint kIOMasterPortDefault = 0;

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern IntPtr IOServiceMatching([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern uint IOServiceGetMatchingService(uint masterPort, IntPtr matching);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOObjectRelease(uint obj);

    // ---- CoreFoundation ----
    private const uint kCFStringEncodingUTF8 = 0x08000100;
    private const int kCFNumberSInt64Type = 4;

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, [MarshalAs(UnmanagedType.LPStr)] string cStr, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern bool CFNumberGetValue(IntPtr number, int theType, out long value);
}