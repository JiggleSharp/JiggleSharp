using System;
using System.Drawing;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using JiggleSharp.Core;
using JiggleSharp.Core.Idle;

namespace JiggleSharp.App;

public partial class MainWindow : Window
{
    private readonly IIdleTimeProvider _idleProvider;

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
    
    public MainWindow(IIdleTimeProvider idleProvider)
    {
        InitializeComponent();
        
        _idleProvider = idleProvider;
        _idleProvider.IdleTimeChanged += OnIdleTimeChanged;
    }
    
    private void OnIdleTimeChanged(object? sender, IdleTimeChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IdleTimeTextBlock.Text = e.IdleTime.ToString();
        });
    }

    private void BtnSaveConfig_OnClick(object? sender, RoutedEventArgs e)
    {
        var config = new ApplicationConfiguration
        {
            TrayIconColor = Color.Red
        };
        ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(config));
    }
}