namespace JiggleSharp.Linux;

public static class Constants
{
    public static string YdotoolServiceNotRunningMessage = 
        "ydotoold.service is not running or was not found. Verify the service is installed and running.";

    public static string YdotoolProxyNotDiscoveredMessage =
        "Failed to parse ydotoold proxy path from service definition.";
}