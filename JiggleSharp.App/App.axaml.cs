using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using JiggleSharp.App.Helpers;
using JiggleSharp.App.ViewModels;
using JiggleSharp.Core.Engine;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;

namespace JiggleSharp.App;

/// <summary>
/// Avalonia application entry point. Owns the top-level object graph for the
/// lifetime of the process: platform services, the jiggle engine, the system
/// tray icon, and the optional main window.
///
/// <para>Startup sequence:</para>
/// <list type="number">
///   <item>
///     <see cref="Initialize"/> — loads XAML resources, creates platform
///     services, loads configuration, and constructs the engine.
///   </item>
///   <item>
///     <see cref="OnFrameworkInitializationCompleted"/> — builds the tray
///     icon, checks idle provider availability, starts monitoring, and
///     configures explicit-shutdown mode so the app lives in the tray without
///     a visible window.
///   </item>
/// </list>
///
/// <para>
/// The main window is created on demand when the user clicks "Settings…" in
/// the tray menu, and destroyed when closed — the app continues running in
/// the background.
/// </para>
/// </summary>
public partial class App : Application
{
    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>ARGB color of the status indicator when the engine is running.</summary>
    private readonly Color _startedIndicatorColor = Color.FromArgb(255, 0, 255, 0);

    /// <summary>ARGB color of the status indicator when the engine is stopped.</summary>
    private readonly Color _stoppedIndicatorColor = Color.FromArgb(255, 139, 0, 0);

    /// <summary>
    /// Absolute path to the JSON configuration file.
    /// Resolves to <c>~/.config/JiggleSharp/config.json</c> on Linux and
    /// <c>%APPDATA%\JiggleSharp\config.json</c> on Windows.
    /// </summary>
    private readonly string _configFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JiggleSharp",
        "config.json");

    // =========================================================================
    // Fields
    // =========================================================================

    /// <summary>
    /// The active application configuration. Loaded from disk during
    /// <see cref="Initialize"/>; replaced in full when the user saves settings.
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
    /// Disposed and rebuilt whenever the configuration changes.
    /// </summary>
    private TrayIcon? _tray;

    /// <summary>
    /// The main settings/status window. Null when hidden; created on demand
    /// and set back to null when the user closes it.
    /// </summary>
    private MainWindow? _mainWindow;

    // =========================================================================
    // Avalonia Application Lifecycle
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

        _engine = new JiggleEngine(
            _config.JigglerEngineOptions,
            _platformServices.IdleTimeProvider,
            _platformServices.InputInjector);
    }

    /// <summary>
    /// Called by Avalonia once the framework is fully initialised and the
    /// application lifetime is available.
    ///
    /// <para>
    /// Validates platform services and environment dependencies, hides the
    /// taskbar indicator, builds the tray icon, then checks whether the idle
    /// provider is available on this system. If available, monitoring is
    /// started; otherwise a warning is written to stderr.
    /// </para>
    ///
    /// <para>
    /// Shutdown mode is set to <see cref="ShutdownMode.OnExplicitShutdown"/>
    /// so closing the main window does not terminate the process.
    /// </para>
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (_platformServices is null)
        {
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

            // Availability check must be synchronous here — the framework has
            // not yet entered the async event loop.
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
    // Main Window Management
    // =========================================================================

    /// <summary>
    /// Shows the main window, creating it if it does not already exist.
    /// If the window is already open, brings it to the foreground instead.
    /// Updates the taskbar indicator to reflect the open window state.
    /// </summary>
    private void CreateMainWindow()
    {
        if (_platformServices is null)
            return;

        // If already open, focus rather than opening a second instance.
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

        var vm = new SettingsViewModel(_config, newConfig =>
        {
            Log.Information("Configuration changed: {Config}", JsonSerializer.Serialize(newConfig));
            _config = newConfig;
            _engine?.Configuration = newConfig.JigglerEngineOptions;

            // Rebuild the tray icon to reflect any icon/color changes.
            _tray?.Dispose();
            _mainWindow?.Close();
            BuildTrayIcon();
            SaveConfiguration();
        });

        _mainWindow.DataContext = vm;
        _mainWindow.Show();
        _platformServices.SystemIntegrationHandler.ShowWindowIndicator();
    }

    // =========================================================================
    // Tray Icon
    // =========================================================================

    /// <summary>
    /// Constructs the system tray icon with the following menu items:
    /// <list type="bullet">
    ///   <item>"Stop/Start JiggleSharp" — toggles the jiggle engine.</item>
    ///   <item>"Settings…"              — opens the main window.</item>
    ///   <item>"Quit"                   — terminates the process.</item>
    /// </list>
    /// The icon reflects the current tray emoji, configured color, and
    /// running/stopped indicator state.
    /// </summary>
    private Task BuildTrayIcon()
    {
        var toggleItem     = new NativeMenuItem("Stop JiggleSharp");
        var settingsItem   = new NativeMenuItem("Settings...");
        var quitItem       = new NativeMenuItem("Quit");

        toggleItem.Click   += ToggleJiggleSharp_Click;
        settingsItem.Click += ShowWindowMenuItem_Click;
        quitItem.Click     += QuitMenuItem_Click;

        _tray = new TrayIcon
        {
            ToolTipText = "JiggleSharp",
            Icon = WindowIconHelper.CreateEmojiIcon(
                _config.TrayIcon,
                _config.TrayIconColor,
                _engine?.IsRunning == true ? _startedIndicatorColor : _stoppedIndicatorColor),
            Menu = new NativeMenu
            {
                Items =
                {
                    toggleItem,
                    new NativeMenuItemSeparator(),
                    settingsItem,
                    new NativeMenuItemSeparator(),
                    quitItem
                }
            }
        };

        return Task.CompletedTask;
    }

    // =========================================================================
    // Tray Menu Event Handlers
    // =========================================================================

    /// <summary>Handles the "Settings…" tray menu item. Opens the main window.</summary>
    private void ShowWindowMenuItem_Click(object? sender, EventArgs e)
        => CreateMainWindow();

    /// <summary>
    /// Handles the "Stop/Start JiggleSharp" tray menu item.
    /// Toggles the engine and updates the tray icon indicator and menu label accordingly.
    /// </summary>
    private void ToggleJiggleSharp_Click(object? sender, EventArgs e)
    {
        var menuItem = sender as NativeMenuItem;

        if (_engine?.IsRunning == true)
        {
            _engine.Stop();
            _tray!.Icon = WindowIconHelper.CreateEmojiIcon(
                _config.TrayIcon, _config.TrayIconColor, _stoppedIndicatorColor);
            if (menuItem is not null) menuItem.Header = "Start JiggleSharp";
        }
        else
        {
            _engine?.Start();
            _tray!.Icon = WindowIconHelper.CreateEmojiIcon(
                _config.TrayIcon, _config.TrayIconColor, _startedIndicatorColor);
            if (menuItem is not null) menuItem.Header = "Stop JiggleSharp";
        }
    }

    /// <summary>
    /// Handles the "Quit" tray menu item.
    /// Flushes the Serilog sink and terminates the process.
    /// </summary>
    private void QuitMenuItem_Click(object? sender, EventArgs e)
    {
        Log.CloseAndFlush();
        Environment.Exit(0);
    }

    // =========================================================================
    // Configuration
    // =========================================================================

    /// <summary>
    /// Loads <see cref="ApplicationConfiguration"/> from the user's application
    /// data directory. Falls back to a default instance if the file does not
    /// exist or cannot be deserialised, logging the error so the user can
    /// investigate a corrupt config without a crash.
    /// </summary>
    /// <returns>
    /// The deserialised configuration, or a new default
    /// <see cref="ApplicationConfiguration"/> on failure.
    /// </returns>
    private ApplicationConfiguration LoadConfiguration()
    {
        if (!File.Exists(_configFilePath))
            return new ApplicationConfiguration();

        try
        {
            var contents = File.ReadAllText(_configFilePath);
            var options  = BuildJsonOptions();
            return JsonSerializer.Deserialize<ApplicationConfiguration>(contents, options)
                   ?? new ApplicationConfiguration();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load configuration from {Path}: {Message}",
                _configFilePath, ex.Message);
            return new ApplicationConfiguration();
        }
    }

    /// <summary>
    /// Serialises the current <see cref="_config"/> and writes it to disk,
    /// creating any missing directories in the path.
    /// </summary>
    /// <returns><c>true</c> on success.</returns>
    /// <exception cref="Exception">Re-throws any I/O or serialisation exception after logging.</exception>
    private bool SaveConfiguration()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath)!);
            var options  = BuildJsonOptions();
            var contents = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configFilePath, contents);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save configuration to {Path}: {Message}",
                _configFilePath, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Builds the shared <see cref="JsonSerializerOptions"/> used for both
    /// serialisation and deserialisation, including the
    /// <see cref="AvaloniaColorJsonConverter"/>.
    /// </summary>
    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new AvaloniaColorJsonConverter());
        return options;
    }
}