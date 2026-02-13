using JiggleSharp.Core.Hosting;

namespace JiggleSharp.Mac;

public class MacEnvironmentValidator : IEnvironmentValidator
{
    public bool VerifyDependencies()
    {
        return true; // Nothing needed currently to verify
    }
}