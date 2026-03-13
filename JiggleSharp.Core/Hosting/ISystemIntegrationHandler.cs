namespace JiggleSharp.Core.Hosting;

public interface ISystemIntegrationHandler
{
    void HideWindowIndicator();
    void ShowWindowIndicator();
    bool RegisterStartupApplication();
    bool DeregisterStartupApplication();
}