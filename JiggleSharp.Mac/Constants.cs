namespace JiggleSharp.Mac;

public static class Constants
{
    /// <summary>
    /// The message to be displayed to the user when the application does not have accessibility
    /// permissions to move the mouse.
    /// </summary>
    public static string AccessibilityPermissionDeniedMessage =
        "JiggleSharp needs Accessibility permission to move the mouse.\n\n" +
        "Grant access in System Settings → Privacy & Security → " +
        "Accessibility, then restart the app.";
}