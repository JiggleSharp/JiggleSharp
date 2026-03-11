using Avalonia.Media;
using JiggleSharp.Core.Engine;

namespace JiggleSharp.App;

public class ApplicationConfiguration
{
    public Color TrayIconColor { get; set; } = Color.FromArgb(255,255,255,255);
    public string TrayIcon { get; set; } = "🖱️";
    public JiggleOptions JigglerEngineOptions { get; set; } = new();
}