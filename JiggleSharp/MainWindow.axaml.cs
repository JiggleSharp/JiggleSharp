using System;
using Avalonia.Controls;
using JiggleSharp.Core.Idle;
using JiggleSharp.ViewModels;

namespace JiggleSharp;

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