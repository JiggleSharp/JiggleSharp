using Avalonia;
using System;
using System.IO;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace JiggleSharp.App;

class Program
{
    public static IHost? Host { get; private set; }
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            InitializeLogging();
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
                .UseSerilog()  // <-- replaces the default MEL providers with Serilog
                .Build();
        
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, $"Application terminating: {ex.Message}");
        }
        finally
        {
            Log.CloseAndFlush(); // ensures buffered logs are written on exit
        }
        
    }

    private static void InitializeLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JiggleSharp", "logs", "jigglesharp.log");
        Console.WriteLine($"Logging to {logPath}");
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 5 * 1024 * 1024)
            .CreateLogger();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}