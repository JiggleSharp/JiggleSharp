using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JiggleSharp.Cli;

/// <summary>
/// Makes console output visible when JiggleSharp is invoked from a terminal.
///
/// <para>
/// JiggleSharp builds as <c>WinExe</c>, so on Windows it runs in the GUI
/// subsystem and is given no console — anything written to stdout from
/// <c>--help</c> or <c>--status</c> would simply vanish. Attaching to the
/// parent process's console and rebinding the standard streams fixes that.
/// One cosmetic wart remains and cannot be avoided for a GUI-subsystem
/// executable: <c>cmd.exe</c> returns the prompt immediately, so the output
/// prints underneath it.
/// </para>
///
/// <para>
/// On macOS and Linux the process already inherits the terminal's streams, so
/// every member here is a no-op.
/// </para>
/// </summary>
internal static class ConsoleBridge
{
    // =========================================================================
    // Native interop
    // =========================================================================

    /// <summary>Value of <c>dwProcessId</c> meaning "the console of the parent process".</summary>
    private const int AttachParentProcess = -1;

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    // =========================================================================
    // Fields
    // =========================================================================

    /// <summary>Whether <see cref="Attach"/> successfully attached a console that must later be freed.</summary>
    private static bool _attached;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Attaches to the invoking terminal's console on Windows and rebinds
    /// <see cref="Console.Out"/> and <see cref="Console.Error"/> to it.
    /// Safe to call repeatedly, and a no-op on non-Windows platforms or when the
    /// process has no parent console (for example when launched from Explorer).
    /// </summary>
    public static void Attach()
    {
        if (!OperatingSystem.IsWindows() || _attached)
            return;

        if (!AttachConsole(AttachParentProcess))
            return;

        _attached = true;

        // Console.Out was bound to the null device when the process started
        // without a console; rebind both streams to the newly attached one.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

        // Separate our output from the prompt cmd.exe has already re-printed.
        Console.WriteLine();
    }

    /// <summary>
    /// Flushes and releases a console attached by <see cref="Attach"/>.
    /// Safe to call when nothing was attached.
    /// </summary>
    public static void Detach()
    {
        if (!OperatingSystem.IsWindows() || !_attached)
            return;

        Console.Out.Flush();
        Console.Error.Flush();
        FreeConsole();

        _attached = false;
    }

    /// <summary>Writes a line to stdout, attaching a console first if needed.</summary>
    public static void WriteLine(string message)
    {
        Attach();
        Console.WriteLine(message);
    }

    /// <summary>Writes a line to stderr, attaching a console first if needed.</summary>
    public static void WriteErrorLine(string message)
    {
        Attach();
        Console.Error.WriteLine(message);
    }
}
