using System.Drawing;
using JiggleSharp.Core.Engine;

namespace JiggleSharp.Core;

public class ApplicationConfiguration
{
    public Color TrayIconColor { get; set; } = Color.White;
    public JiggleOptions JigglerEngineOptions { get; set; } = new();
}