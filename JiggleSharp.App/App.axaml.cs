using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JiggleSharp.Core.Engine;

namespace JiggleSharp.App;

public partial class App : Application
{
    private IPlatformServices? _platformServices;
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _platformServices = PlatformServicesFactory.Create();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_platformServices is not null)
        {
            

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (_platformServices != null)
                {
                    desktop.Exit += async (_, __) => await _platformServices.IdleTimeProvider.StopAsync();
                    
                    desktop.MainWindow = new MainWindow(_platformServices.IdleTimeProvider);
                    
                    _platformServices.IdleTimeProvider.Start();
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
        else
        {
            Environment.Exit(0);
        }
    }
}