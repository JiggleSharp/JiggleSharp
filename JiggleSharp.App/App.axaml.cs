using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using JiggleSharp.Core;
using JiggleSharp.Core.Engine;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using Ursa.Controls;

namespace JiggleSharp.App;

/// <summary>
/// Avalonia application entry point. Owns the top-level object graph for the
/// lifetime of the process: platform services, the jiggle engine, the system
/// tray icon, and the optional main window.
///
/// Startup sequence:
///   1. <see cref="Initialize"/> — loads XAML resources, creates platform
///      services, loads configuration, and constructs the engine.
///   2. <see cref="OnFrameworkInitializationCompleted"/> — builds the tray
///      icon, checks idle provider availability, starts monitoring, and
///      configures explicit-shutdown mode so the app lives in the tray without
///      a visible window.
///
/// The main window is created on demand when the user clicks "Open JiggleSharp"
/// in the tray menu, and destroyed when closed — the app continues running in
/// the background.
/// </summary>
public partial class App : Application
{
    // =========================================================================
    // Fields
    // =========================================================================

    /// <summary>
    /// The application configuration loaded either from the user's data or from
    /// default values in the <see cref="ApplicationConfiguration"/> class.
    /// </summary>
    private ApplicationConfiguration _config = new();
    
    /// <summary>
    /// Platform-specific service implementations (input injector, idle
    /// provider, system integration, logger). Null only between object
    /// construction and <see cref="Initialize"/>.
    /// </summary>
    private IPlatformServices? _platformServices;

    /// <summary>
    /// The core engine that watches idle time and performs mouse jiggles.
    /// Constructed in <see cref="Initialize"/> once services are available.
    /// </summary>
    private JiggleEngine? _engine;

    /// <summary>
    /// System tray icon. Created in <see cref="BuildTrayIcon"/> during
    /// framework initialization and lives for the duration of the process.
    /// </summary>
    private TrayIcon? _tray;

    /// <summary>
    /// The main settings/status window. Null when hidden; created on demand
    /// and set back to null when the user closes it.
    /// </summary>
    private MainWindow? _mainWindow;
    
    // =========================================================================
    // Avalonia application lifecycle
    // =========================================================================

    /// <summary>
    /// Called by the Avalonia framework before the event loop starts.
    /// Loads XAML resources, initialises platform services, reads persisted
    /// configuration, and wires up the <see cref="JiggleEngine"/>.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        _platformServices = PlatformServicesFactory.Create();

        _config = LoadConfiguration();
        

        // TODO: remove once a proper settings UI exposes this field.
        _config.JigglerEngineOptions.IdleTimeout = TimeSpan.FromSeconds(10);

        _engine = new JiggleEngine(
            _config.JigglerEngineOptions,
            _platformServices.IdleTimeProvider,
            _platformServices.InputInjector);
    }

    /// <summary>
    /// Called by Avalonia once the framework is fully initialised and the
    /// application lifetime is available.
    ///
    /// Hides the taskbar indicator, builds the tray icon, then checks whether
    /// the idle provider is available on this system. If available, monitoring
    /// is started; otherwise a warning is written to stderr.
    ///
    /// Shutdown mode is set to <see cref="ShutdownMode.OnExplicitShutdown"/>
    /// so closing the main window does not terminate the process — the app
    /// continues running via the tray icon.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (_platformServices is null)
        {
            // Platform services could not be created; nothing useful can run.
            Log.Fatal("Failed to initialize platform services: null _platformServices object.");
            Environment.Exit(0);
            return;
        }

        if (!_platformServices.EnvironmentValidator.VerifyDependencies())
        {
            Log.Fatal("Failed to initialize platform services: dependencies not running or not found.");
            Dispatcher.UIThread.Post(async () =>
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    "Missing Dependencies",
                    "One or more dependencies are not running or could not be found. Check the logs for more " +
                    "information. The application will now exit.",
                    ButtonEnum.Ok,
                    Icon.Error);

                await box.ShowAsync();
                await Program.Host!.StopAsync();
                Environment.Exit(1);
            });
            
            return;
        }
        
        _platformServices.SystemIntegrationHandler.HideWindowIndicator();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            BuildTrayIcon();

            // Stop the idle provider gracefully when the application exits.
            desktop.Exit += async (_, _) =>
                await _platformServices.IdleTimeProvider.StopAsync();

            // Availability check must be synchronous at this point; the
            // framework has not yet entered the async event loop.
            var available = Task.Run(() => _platformServices.IdleTimeProvider.IsAvailableAsync())
                .GetAwaiter()
                .GetResult();

            if (available)
                _platformServices.IdleTimeProvider.Start();
            else
                Console.Error.WriteLine("IdleTimeProvider is not available on this system.");

            // Keep the process alive after the main window is closed.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // =========================================================================
    // Main window management
    // =========================================================================

    /// <summary>
    /// Shows the main window, creating it if it does not already exist.
    /// If the window is already open, brings it to the foreground instead.
    /// Updates the taskbar indicator to reflect the open window.
    /// </summary>
    private void CreateMainWindow()
    {
        if (_platformServices is null)
            return;

        // If the window is already open, focus it rather than opening a second.
        if (_mainWindow is not null)
        {
            _mainWindow.Activate();
            return;
        }

        _mainWindow = new MainWindow(_platformServices.IdleTimeProvider);
        _mainWindow.Closed += (_, _) =>
        {
            _platformServices.SystemIntegrationHandler.HideWindowIndicator();
            _mainWindow = null;
        };
        _mainWindow.ConfigurationChanged += (sender, args) =>
        {
            Log.Information($"Configuration changed: {JsonSerializer.Serialize(args.NewConfiguration)}");
            _config = args.NewConfiguration;
            _tray?.Dispose();
            BuildTrayIcon();
        };

        _mainWindow.Show();
        _platformServices.SystemIntegrationHandler.ShowWindowIndicator();
    }

    // =========================================================================
    // Tray icon
    // =========================================================================

    /// <summary>
    /// Constructs the system tray icon with a two-item menu:
    ///   - "Open JiggleSharp" — shows the main window.
    ///   - "Quit"             — terminates the process.
    /// </summary>
    private Task BuildTrayIcon()
    {
        var showWindowItem = new NativeMenuItem("Open JiggleSharp");
        var quitItem       = new NativeMenuItem("Quit");

        showWindowItem.Click += ShowWindowMenuItem_Click;
        quitItem.Click       += QuitMenuItem_Click;

        _tray = new TrayIcon
        {
            ToolTipText = "JiggleSharp",
            Icon        = WindowIconHelper.CreateEmojiIcon("🖱️", _config.TrayIconColor),
            Menu        = new NativeMenu
            {
                Items =
                {
                    showWindowItem,
                    new NativeMenuItemSeparator(),
                    quitItem
                }
            }
        };

        return Task.CompletedTask;
    }

    // =========================================================================
    // Tray menu event handlers
    // =========================================================================

    /// <summary>Handles the "Open JiggleSharp" tray menu item.</summary>
    private void ShowWindowMenuItem_Click(object? sender, EventArgs e)
    {
        CreateMainWindow();
    }

    /// <summary>Handles the "Quit" tray menu item.</summary>
    private void QuitMenuItem_Click(object? sender, EventArgs e)
    {
        Log.CloseAndFlush();
        Environment.Exit(0);
    }

    // =========================================================================
    // Configuration
    // =========================================================================

    /// <summary>
    /// Loads <see cref="JiggleOptions"/> from the user's application data
    /// directory (<c>%APPDATA%/JiggleSharp/config.json</c> on Windows,
    /// <c>~/.config/JiggleSharp/config.json</c> on Linux).
    ///
    /// Falls back to a default <see cref="JiggleOptions"/> instance if the
    /// file does not exist or cannot be deserialised, logging the error so
    /// the user can investigate a corrupt config without a crash.
    /// </summary>
    private ApplicationConfiguration LoadConfiguration()
    {
        var filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JiggleSharp",
            "config.json");

        if (!File.Exists(filePath))
            return new ApplicationConfiguration();

        try
        {
            var contents = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ApplicationConfiguration>(contents) ?? new ApplicationConfiguration();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to load configuration from {filePath}: {ex.Message}");
            return new ApplicationConfiguration();
        }
    }
}