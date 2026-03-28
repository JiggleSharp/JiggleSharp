using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Skia.Lottie;

namespace JiggleSharp;

public partial class AboutPage : UserControl
{
    public string ApplicationVersion => Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? string.Empty;

    public AboutPage()
    {
        InitializeComponent();
        DataContext = this;
        
        // Ugly hack to make the milacoder.dev logo reappear when switching tabs
        AttachedToVisualTree += (_, e) =>
        {
            MilaLogo.SetValue(Lottie.PathProperty, null);
            MilaLogo.SetValue(Lottie.PathProperty, "/Resources/Animations/miladev-logo.json");        };
    }
}