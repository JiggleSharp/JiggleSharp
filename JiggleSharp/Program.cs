using Avalonia;
using System;
using System.IO;
using JiggleSharp.Cli;
using JiggleSharp.Core.Ipc;
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

    /// <summary>Process exit code used when a running instance reported an error.</summary>
    private const int ExitCodeRemoteError = 1;

    /// <summary>Process exit code used when the command line could not be parsed.</summary>
    private const int ExitCodeUsageError = 2;

    // =========================================================================
    // Properties
    // =========================================================================

    /// <summary>
    /// The application's generic host. Provides the DI container, hosted
    /// services, and the Serilog-backed logging pipeline.
    /// Available after <see cref="Main"/> initialises it; null before that point.
    /// </summary>
    public static IHost? Host { get; private set; }

    /// <summary>
    /// Set when the process was launched with <c>--start</c> or <c>--toggle</c>
    /// and found no running instance to forward the command to. The application
    /// starts the jiggle engine on launch regardless of the persisted
    /// <c>StartEngineOnApplicationStart</c> setting.
    /// </summary>
    internal static bool ForceEngineStart { get; private set; }

    // =========================================================================
    // Entry Point
    // =========================================================================

    /// <summary>
    /// Application entry point. Handles the command line first — which may
    /// forward a command to an already-running instance and exit without ever
    /// initialising Avalonia — then initialises Serilog, builds the generic
    /// host, and launches the Avalonia desktop lifetime.
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
        // Runs before logging is configured so that a short-lived client
        // invocation such as `JiggleSharp --status` prints only its answer,
        // without the Serilog console sink writing over it.
        if (HandleCommandLine(args))
            return;

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
    // Command Line
    // =========================================================================

    /// <summary>
    /// Parses the command line and, where possible, satisfies it without
    /// starting the UI.
    ///
    /// <para>
    /// Every invocation — including a plain launch with no flags — first probes
    /// for a running instance over the named pipe, because a newly started
    /// process cannot observe or change the tray instance's in-memory engine
    /// state any other way. If an instance answers, the command is forwarded to
    /// it and this process exits.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> if the process has finished its work and should exit;
    /// <c>false</c> if it should go on to launch the tray application.
    /// </returns>
    private static bool HandleCommandLine(string[] args)
    {
        var options = CommandLineOptions.Parse(args);

        switch (options.Action)
        {
            case CliAction.Error:
                ConsoleBridge.WriteErrorLine(options.Error!);
                ConsoleBridge.WriteErrorLine(string.Empty);
                Console.Error.WriteLine(CommandLineOptions.HelpText);
                ConsoleBridge.Detach();
                Environment.Exit(ExitCodeUsageError);
                return true;

            case CliAction.Help:
                ConsoleBridge.WriteLine(CommandLineOptions.HelpText);
                ConsoleBridge.Detach();
                Environment.Exit(0);
                return true;
        }

        var command  = ToIpcCommand(options.Action);
        var response = IpcClient.SendAsync(command).GetAwaiter().GetResult();

        return response is null
            ? HandleNoRunningInstance(options.Action)
            : HandleRunningInstance(options.Action, response);
    }

    /// <summary>
    /// Maps a CLI action onto the protocol command sent to the running instance.
    /// A plain launch sends <see cref="IpcProtocol.Status"/>, using it purely as
    /// a single-instance probe that leaves the engine untouched.
    /// </summary>
    private static string ToIpcCommand(CliAction action) => action switch
    {
        CliAction.Start  => IpcProtocol.Start,
        CliAction.Stop   => IpcProtocol.Stop,
        CliAction.Toggle => IpcProtocol.Toggle,
        _                => IpcProtocol.Status
    };

    /// <summary>
    /// Decides what to do when no instance answered the pipe.
    /// <c>--start</c> and <c>--toggle</c> fall through to launching the
    /// application with the engine active; <c>--stop</c> and <c>--status</c>
    /// report the situation and exit without launching anything.
    /// </summary>
    /// <returns><c>true</c> if the process should exit.</returns>
    private static bool HandleNoRunningInstance(CliAction action)
    {
        switch (action)
        {
            case CliAction.Start:
            case CliAction.Toggle:
                ForceEngineStart = true;
                return false;

            case CliAction.Launch:
                return false;

            default:
                ConsoleBridge.WriteLine("JiggleSharp is not running.");
                ConsoleBridge.Detach();
                Environment.Exit(0);
                return true;
        }
    }

    /// <summary>
    /// Reports the outcome of a command forwarded to the running instance and
    /// exits. The response carries the engine state *after* the command was
    /// applied, which is what lets <c>--toggle</c> describe what it did.
    /// </summary>
    /// <returns><c>true</c> — this process has always finished its work here.</returns>
    private static bool HandleRunningInstance(CliAction action, string response)
    {
        if (response.StartsWith(IpcProtocol.ErrorPrefix, StringComparison.Ordinal))
        {
            ConsoleBridge.WriteErrorLine(
                $"JiggleSharp reported an error: {response[IpcProtocol.ErrorPrefix.Length..]}");
            ConsoleBridge.Detach();
            Environment.Exit(ExitCodeRemoteError);
            return true;
        }

        var jiggling = response.StartsWith(IpcProtocol.ResponseRunning, StringComparison.Ordinal);

        ConsoleBridge.WriteLine(action switch
        {
            CliAction.Start  => "Jiggling started.",
            CliAction.Stop   => "Jiggling stopped.",
            CliAction.Toggle => jiggling ? "Jiggling started." : "Jiggling stopped.",
            CliAction.Status => jiggling
                ? "JiggleSharp is running and jiggling."
                : "JiggleSharp is running; jiggling is stopped.",
            _ => "JiggleSharp is already running."
        });

        ConsoleBridge.Detach();
        Environment.Exit(0);
        return true;
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