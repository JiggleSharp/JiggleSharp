using JiggleSharp.Core.Hosting;

namespace JiggleSharp.Mac;

public class MacEnvironmentValidator : IEnvironmentValidator
{
    public (bool success, string error) VerifyDependencies()
    {
        return System.MacAccessibilityHelper.CheckAccessibility() 
            ? (true, string.Empty) 
            : (false, Constants.AccessibilityPermissionDeniedMessage);
    }
}