using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using JiggleSharp.Core.Engine;
using JiggleSharp.Core.Hosting;

namespace JiggleSharp;

public class ApplicationConfiguration
{
    [JsonIgnore]
    public ISystemIntegrationHandler? SystemIntegrationHandler { get; set; }
    public bool StartEngineOnApplicationStart { get; set; } = true;
    [JsonIgnore]
    public bool StartJiggleSharpOnSystemStartup { get; set; }
    public Color TrayIconColor { get; set; } = Color.FromArgb(255,255,255,255);
    public string TrayIcon { get; set; } = "🖱️";
    public JiggleOptions JigglerEngineOptions { get; set; } = new();
    public ApplicationConfiguration() { }

    public ApplicationConfiguration(ISystemIntegrationHandler systemIntegrationHandler)
    {
        SystemIntegrationHandler = systemIntegrationHandler;
    }
}