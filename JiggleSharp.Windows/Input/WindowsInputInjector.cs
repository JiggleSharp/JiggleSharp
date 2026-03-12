using System.Runtime.InteropServices;
using JiggleSharp.Core.Input;

namespace JiggleSharp.Windows.Input;

/// <summary>
/// Windows implementation of <see cref="IInputInjector"/> using <c>SendInput</c>
/// from user32.dll to inject relative mouse movement events into the input stream.
/// </summary>
public class WindowsInputInjector : IInputInjector
{
    // -------------------------------------------------------------------------
    // IInputInjector
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public event EventHandler<Exception>? InputInjectorFailure;

    /// <inheritdoc/>
    /// <remarks>
    /// Injects a relative mouse movement via <c>SendInput</c>. <paramref name="dx"/>
    /// and <paramref name="dy"/> are pixel deltas from the current cursor position.
    /// Failures are surfaced via <see cref="InputInjectorFailure"/> rather than thrown.
    /// </remarks>
    public Task MoveMouseAsync(int dx, int dy, CancellationToken ct)
    {
        try
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_MOVE,
                    mouseData = 0,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            };

            uint sent = SendInput(1, ref input, Marshal.SizeOf<INPUT>());

            if (sent == 0)
                throw new InvalidOperationException($"SendInput failed: {Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            InputInjectorFailure?.Invoke(this, ex);
        }

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // P/Invoke
    // -------------------------------------------------------------------------

    /// <summary>Mouse input event type, passed as <c>INPUT.type</c>.</summary>
    private const uint INPUT_MOUSE = 0;

    /// <summary>
    /// Flag indicating relative mouse movement. Combine with
    /// <c>MOUSEEVENTF_VIRTUALDESK | 0x8000</c> for absolute coordinates.
    /// </summary>
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    /// <summary>
    /// Describes a mouse input event. Part of a union within <see cref="INPUT"/>;
    /// also accommodates <c>KEYBDINPUT</c> and <c>HARDWAREINPUT</c> in other contexts.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nint dwExtraInfo;
    }

    /// <summary>Used by <c>SendInput</c> to describe a single synthesized input event.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern nint GetMessageExtraInfo();
}