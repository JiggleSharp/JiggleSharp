using System.Runtime.InteropServices;
using JiggleSharp.Core.Idle;

namespace JiggleSharp.Mac.Idle;

/// <summary>
/// macOS implementation of <see cref="IIdleTimeProvider"/> that reads the
/// system HID idle time from the IORegistry.
///
/// How it works:
///   1. <see cref="Start"/> launches a background loop that ticks every second.
///   2. Each tick reads <c>HIDIdleTime</c> from the <c>IOHIDSystem</c> IORegistry
///      entry, which reflects the time since the last real hardware input event
///      (keyboard, mouse, trackpad, etc.).
///   3. The idle duration is broadcast via <see cref="IdleTimeChanged"/>, which
///      <see cref="JiggleSharp.Core.Engine.JiggleEngine"/> subscribes to.
/// </summary>
public class MacIdleTimeProvider : IIdleTimeProvider
{
    // =========================================================================
    // Events
    // =========================================================================

    /// <inheritdoc/>
    public event EventHandler<IdleTimeChangedEventArgs>? IdleTimeChanged;

    // =========================================================================
    // State
    // =========================================================================

    private CancellationTokenSource? _cts;
    private Task?                    _loop;

    // =========================================================================
    // IIdleTimeProvider
    // =========================================================================

    /// <inheritdoc/>
    public void Start()
    {
        if (_loop != null) return;

        _cts  = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (_cts == null) return;

        await _cts.CancelAsync();
        try
        {
            if (_loop != null)
                await _loop;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _loop = null;
            _cts.Dispose();
            _cts  = null;
        }
    }

    /// <summary>
    /// Reads the current HID idle time from the IORegistry and fires
    /// <see cref="IdleTimeChanged"/> with the result.
    /// </summary>
    public Task<TimeSpan> GetIdleTimeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var idleNs      = GetHidIdleTimeNanoseconds();
        var newIdleTime = TimeSpan.FromSeconds(idleNs / 1_000_000_000.0);

        IdleTimeChanged?.Invoke(this, new IdleTimeChangedEventArgs(newIdleTime));

        return Task.FromResult(newIdleTime);
    }

    /// <inheritdoc/>
    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    // =========================================================================
    // Background loop
    // =========================================================================

    /// <summary>
    /// Polls <see cref="GetIdleTimeAsync"/> once per second until cancelled.
    /// </summary>
    private async Task LoopAsync(CancellationToken ct = default)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(ct))
            _ = await GetIdleTimeAsync(ct);
    }

    // =========================================================================
    // IORegistry idle time reader
    // =========================================================================

    /// <summary>
    /// Reads <c>HIDIdleTime</c> from the <c>IOHIDSystem</c> IORegistry entry.
    /// Returns the idle duration in nanoseconds as a <c>uint64</c>.
    ///
    /// The value reflects time since the last real hardware input event and is
    /// reset to zero by any keyboard, mouse, or trackpad activity.
    /// </summary>
    private static ulong GetHidIdleTimeNanoseconds()
    {
        IntPtr matching = IOServiceMatching("IOHIDSystem");
        if (matching == IntPtr.Zero)
            throw new InvalidOperationException("IOServiceMatching(IOHIDSystem) failed.");

        uint service = IOServiceGetMatchingService(kIOMasterPortDefault, matching);
        if (service == 0)
            throw new InvalidOperationException("IOServiceGetMatchingService returned 0 — IOHIDSystem not found.");

        try
        {
            IntPtr key = CFStringCreateWithCString(
                IntPtr.Zero, "HIDIdleTime", kCFStringEncodingUTF8);
            if (key == IntPtr.Zero)
                throw new InvalidOperationException("CFStringCreateWithCString failed.");

            try
            {
                IntPtr value = IORegistryEntryCreateCFProperty(service, key, IntPtr.Zero, 0);
                if (value == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "IORegistryEntryCreateCFProperty(HIDIdleTime) returned null.");

                try
                {
                    // HIDIdleTime is a CFNumber containing a uint64 nanosecond count.
                    // We read it as a signed int64 and treat negative values as zero
                    // to guard against any unexpected wrapping behavior.
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

    // =========================================================================
    // P/Invoke — IOKit
    // =========================================================================

    /// <summary>The default IOKit master port. Pass 0 to use the default.</summary>
    private const uint kIOMasterPortDefault = 0;

    /// <summary>Returns a matching dictionary for the named IOService class.</summary>
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern IntPtr IOServiceMatching(
        [MarshalAs(UnmanagedType.LPStr)] string name);

    /// <summary>Returns the first IOService object matching the given dictionary.</summary>
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern uint IOServiceGetMatchingService(
        uint masterPort, IntPtr matching);

    /// <summary>Returns the value of an IORegistry entry property as a CF type.</summary>
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern IntPtr IORegistryEntryCreateCFProperty(
        uint entry, IntPtr key, IntPtr allocator, uint options);

    /// <summary>Releases an IOKit object obtained from the registry.</summary>
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    private static extern int IOObjectRelease(uint obj);

    // =========================================================================
    // P/Invoke — CoreFoundation
    // =========================================================================

    /// <summary>UTF-8 string encoding constant for CFString creation.</summary>
    private const uint kCFStringEncodingUTF8 = 0x08000100;

    /// <summary>CFNumber type identifier for a signed 64-bit integer.</summary>
    private const int kCFNumberSInt64Type = 4;

    /// <summary>Creates a CFString from a C string using the specified encoding.</summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(
        IntPtr alloc, [MarshalAs(UnmanagedType.LPStr)] string cStr, uint encoding);

    /// <summary>Releases a Core Foundation object.</summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    /// <summary>Extracts the numeric value from a CFNumber into a native type.</summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern bool CFNumberGetValue(
        IntPtr number, int theType, out long value);
}