using JiggleSharp.Core.Input;
using System.Runtime.InteropServices;
using JiggleSharp.Mac.System;
using Serilog;

namespace JiggleSharp.Mac.Input;

/// <summary>
/// macOS implementation of <see cref="IInputInjector"/> that uses Core Graphics
/// to inject synthetic mouse movement events via the HID event tap.
///
/// Movement is performed using absolute screen coordinates internally, derived
/// by reading the real cursor position from the OS before each move and applying
/// the requested delta on top of it. The resulting position is clamped to the
/// main display bounds to prevent the cursor from becoming stuck at screen edges.
///
/// Requires Accessibility permission granted in
/// System Settings → Privacy &amp; Security → Accessibility.
/// </summary>
public class MacInputInjector : IInputInjector
{
    // =========================================================================
    // Events
    // =========================================================================

    /// <summary>
    /// Raised when a non-recoverable input injection failure occurs, such as
    /// Accessibility permission being denied or a Core Graphics event failure.
    /// </summary>
    public event EventHandler<Exception>? InputInjectorFailure;

    // =========================================================================
    // Native structs
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public CGPoint Origin;
        public CGSize  Size;
    }

    // =========================================================================
    // P/Invoke — Core Graphics
    // =========================================================================

    /// <summary>Posts a Core Graphics event to a HID event tap.</summary>
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(uint tap, IntPtr @event);

    /// <summary>Creates a mouse event at the specified absolute screen position.</summary>
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateMouseEvent(
        IntPtr source, uint mouseType, CGPoint mouseCursorPosition, uint mouseButton);

    /// <summary>Returns the absolute screen location encoded in a CGEvent.</summary>
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    /// <summary>Creates a new CGEvent with no type. Used as a zero-cost way to
    /// query the current cursor position via <see cref="CGEventGetLocation"/>.</summary>
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    /// <summary>Returns the bounds of the specified display in global screen coordinates.</summary>
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGRect CGDisplayBounds(uint display);

    /// <summary>Returns the display ID of the main display.</summary>
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern uint CGMainDisplayID();

    /// <summary>Releases a Core Foundation object.</summary>
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CFRelease(IntPtr cf);

    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>CGEventType: mouse moved (no button held).</summary>
    private const uint kCGEventMouseMoved = 5;

    /// <summary>CGEventTapLocation: hardware input (HID) event tap.
    /// Events posted here are indistinguishable from real hardware input.</summary>
    private const uint kCGHIDEventTap = 0;

    /// <summary>CGMouseButton: left mouse button (unused for move events,
    /// required as a parameter by CGEventCreateMouseEvent).</summary>
    private const uint kCGMouseButtonLeft = 0;

    // =========================================================================
    // IInputInjector
    // =========================================================================

    /// <summary>
    /// Moves the mouse cursor by (<paramref name="dx"/>, <paramref name="dy"/>) pixels
    /// relative to its current position.
    ///
    /// Internally converts the delta to an absolute screen coordinate by reading
    /// the real cursor position from the OS, then clamps the result to the main
    /// display bounds before posting the event.
    /// </summary>
    public Task MoveMouseAsync(int dx, int dy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!MacAccessibilityHelper.CheckAccessibility())
        {
            var error = new InputInjectorException(Constants.AccessibilityPermissionDeniedMessage);
            Log.Fatal(error.Message);
            InputInjectorFailure?.Invoke(this, error);
            return Task.CompletedTask;
        }

        // Read the real cursor position from the OS so our absolute target
        // is always correct regardless of where the cursor actually is.
        var currentPos = GetCurrentMousePosition();

        var newPos = ClampToDisplay(new CGPoint
        {
            X = currentPos.X + dx,
            Y = currentPos.Y + dy
        });

        var moveEvent = CGEventCreateMouseEvent(
            IntPtr.Zero, kCGEventMouseMoved, newPos, kCGMouseButtonLeft);

        if (moveEvent == IntPtr.Zero)
            throw new InvalidOperationException(
                "CGEventCreateMouseEvent returned null — Accessibility permission may be missing.");

        try
        {
            CGEventPost(kCGHIDEventTap, moveEvent);
        }
        catch (Exception ex)
        {
            var error = new InputInjectorException(
                $"An error occurred while attempting to move the mouse: {ex.Message}", ex);
            Log.Error(error.Message);
            InputInjectorFailure?.Invoke(this, error);
            return Task.FromException(ex);
        }
        finally
        {
            CFRelease(moveEvent);
        }

        return Task.CompletedTask;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Returns the current absolute cursor position by creating a null CGEvent
    /// and reading its location, which the OS populates with the current cursor
    /// coordinates. The event is released immediately after the read.
    /// </summary>
    private CGPoint GetCurrentMousePosition()
    {
        var nullEvent = CGEventCreate(IntPtr.Zero);
        try
        {
            return CGEventGetLocation(nullEvent);
        }
        finally
        {
            if (nullEvent != IntPtr.Zero)
                CFRelease(nullEvent);
        }
    }

    /// <summary>
    /// Clamps <paramref name="p"/> to the bounds of the main display, keeping
    /// the cursor at least one pixel inside each edge. This prevents the cursor
    /// from becoming stuck in a corner when WindMouse overshoots the screen boundary.
    /// </summary>
    private CGPoint ClampToDisplay(CGPoint p)
    {
        var bounds = CGDisplayBounds(CGMainDisplayID());
        return new CGPoint
        {
            X = Math.Clamp(p.X, bounds.Origin.X, bounds.Origin.X + bounds.Size.Width  - 1),
            Y = Math.Clamp(p.Y, bounds.Origin.Y, bounds.Origin.Y + bounds.Size.Height - 1)
        };
    }
}