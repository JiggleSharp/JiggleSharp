using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ursa.Controls;

namespace JiggleSharp.App;

// A simple reusable message dialog that inherits your app theme naturally
public class MessageDialog : UrsaWindow
{
    private readonly TaskCompletionSource _tcs = new();
    public Task Result => _tcs.Task;
    
    public MessageDialog(string title, string message)
    {
        Title         = title;
        Width         = 400;
        SizeToContent = SizeToContent.Height;
        CanResize     = false;
        Padding       = new Thickness(0, 20, 0, 0);

        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 16 };

        stack.Children.Add(new TextBlock
        {
            Text       = title,
            FontSize   = 16,
            FontWeight = FontWeight.Bold,
            Margin     = new Thickness(0, 0, 0, 4)
        });
        
        stack.Children.Add(new TextBlock
        {
            Text        = message,
            TextWrapping = TextWrapping.Wrap
        });

        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) =>
        {
            _tcs.TrySetResult();
            Close();
        };
        stack.Children.Add(ok);

        // Also complete if the window is closed via the title bar X
        Closed += (_, _) => _tcs.TrySetResult();
        
        Content = stack;
    }
}