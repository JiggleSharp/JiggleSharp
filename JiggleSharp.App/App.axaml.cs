using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JiggleSharp.Core.Engine;

namespace JiggleSharp.App;

public partial class App : Application
{
    private IPlatformServices? _platformServices;
    private JiggleEngine? _engine;
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _platformServices = PlatformServicesFactory.Create();
        _engine = new JiggleEngine(_platformServices.SystemLog, 
            _platformServices.IdleTimeProvider, 
            _platformServices.InputInjector);
    }

    /// <summary>
    /// Called when the Avalonia framework has been initialized
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (_platformServices is not null)
        {
            _platformServices.SystemIntegrationHandler.HideWindowIndicator();
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                BuildTrayIcon();
                
                if (_platformServices != null)
                {
                    desktop.Exit += async (_, __) => await _platformServices?.IdleTimeProvider.StopAsync()!;

                    var available = Task.Run(() => _platformServices?.IdleTimeProvider.IsAvailableAsync())
                        .GetAwaiter()
                        .GetResult();

                    if (available)
                        _platformServices?.IdleTimeProvider.Start();
                    else
                        Console.Error.WriteLine("IdleTimeProvider is not available");
                }
                
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            base.OnFrameworkInitializationCompleted();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Creates the main window and displays a dock/taskbar icon for the window
    /// </summary>
    private void CreateMainWindow()
    {
        if (_platformServices is not null)
        {
            if (_mainWindow is not null)
            {
                _mainWindow.Activate();
                return;
            }

            _mainWindow = new MainWindow(_platformServices?.IdleTimeProvider!);
            _mainWindow.Closed += (_, _) =>
            {
                _platformServices?.SystemIntegrationHandler.HideWindowIndicator();
                _mainWindow = null;
            };
            _mainWindow.Show();
            
            _platformServices?.SystemIntegrationHandler.ShowWindowIndicator();
        }
    }
    
    /// <summary>
    /// Builds a menu for the system tray icon
    /// </summary>
    private void BuildTrayIcon()
    {
        var showWindowAction = new NativeMenuItem("Open JiggleSharp");
        var quitAction = new NativeMenuItem("Quit");
        showWindowAction.Click += ShowWindowMenuItem_Click;
        quitAction.Click += QuitMenuItem_Click;
        
        _tray = new TrayIcon
        {
            ToolTipText = "JiggleSharp",
            Icon = WindowIconHelper.CreateEmojiIcon("🖱️"),
            Menu = new NativeMenu
            {
                Items =
                {
                    showWindowAction,
                    new NativeMenuItemSeparator(),
                    quitAction
                }
            }
        };
    }

    /// <summary>
    /// Handles clicking the "Open JigglerSharp" menu item in the tray icon
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ShowWindowMenuItem_Click(object? sender, EventArgs e)
    {
        CreateMainWindow();
    }
    
    /// <summary>
    /// Handles clicking the "Quit" menu item in the tray icon
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void QuitMenuItem_Click(object? sender, EventArgs e)
    {
        Environment.Exit(0);
    }
}