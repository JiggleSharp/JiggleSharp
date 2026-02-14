using Avalonia.Controls;
using Avalonia.Threading;
using JiggleSharp.Core.Idle;

namespace JiggleSharp.App;

public partial class MainWindow : Window
{
    private readonly IIdleTimeProvider _idleProvider;

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
}