using Avalonia;
using System;
using System.IO;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace JiggleSharp;

/// <summary>
/// Application entry point. Responsible for bootstrapping the host, logging,
/// and the Avalonia desktop lifetime.
///
/// <para>
/// No Avalonia APIs, third-party APIs, or <see cref="System.Threading.SynchronizationContext"/>-
/// reliant code may be called before <see cref="BuildAvaloniaApp"/> is invoked —
/// the framework is not yet initialised at that point.
/// </para>
/// </summary>
internal class Program
{
    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>Maximum size of a single rolling log file in bytes (5 MB).</summary>
    private const long LogFileSizeLimit = 5 * 1024 * 1024;

    /// <summary>Number of daily log files to retain before the oldest is deleted.</summary>
    private const int LogRetainedFileCount = 7;

    // =========================================================================
    // Properties
    // =========================================================================

    /// <summary>
    /// The application's generic host. Provides the DI container, hosted
    /// services, and the Serilog-backed logging pipeline.
    /// Available after <see cref="Main"/> initialises it; null before that point.
    /// </summary>
    public static IHost? Host { get; private set; }

    // =========================================================================
    // Entry Point
    // =========================================================================

    /// <summary>
    /// Application entry point. Initialises Serilog, builds the generic host,
    /// then launches the Avalonia desktop lifetime.
    ///
    /// <para>
    /// Any unhandled top-level exception is logged as fatal before the process
    /// exits. The Serilog sink is always flushed in the <c>finally</c> block to
    /// ensure buffered log entries are written even on a crash.
    /// </para>
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            InitializeLogging();

            Host = new HostBuilder()
                .UseSerilog()
                .Build();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminating: {Message}", ex.Message);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// Configures the global Serilog logger with a console sink and a
    /// size-capped, daily rolling file sink.
    ///
    /// <para>
    /// Log files are written to
    /// <c>~/.local/share/JiggleSharp/logs/jigglesharp.log</c> on Linux and
    /// <c>%LOCALAPPDATA%\JiggleSharp\logs\jigglesharp.log</c> on Windows.
    /// Up to <see cref="LogRetainedFileCount"/> daily files are kept; each is
    /// capped at <see cref="LogFileSizeLimit"/> bytes.
    /// </para>
    /// </summary>
    private static void InitializeLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JiggleSharp", "logs", "jigglesharp.log");

        Console.WriteLine($"Logging to {logPath}");
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: LogRetainedFileCount,
                fileSizeLimitBytes: LogFileSizeLimit)
            .CreateLogger();
    }

    /// <summary>
    /// Builds and returns the Avalonia <see cref="AppBuilder"/>.
    /// Also used by the Avalonia visual designer — do not remove.
    /// </summary>
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}