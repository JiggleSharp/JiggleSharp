using System;
using System.Drawing;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using JiggleSharp.App.ViewModels;
using JiggleSharp.Core;
using JiggleSharp.Core.Idle;

namespace JiggleSharp.App;

public partial class MainWindow : Window
{
    
    public MainWindow(IIdleTimeProvider idleProvider)
    {
        InitializeComponent();
    }
    
    // Give the ViewModel a way to close the window without
    // the VM needing a direct reference to the Window
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel vm)
        {
            vm.CloseRequested += Close; // see below
        }
    }
}