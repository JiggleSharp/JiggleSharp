namespace JiggleSharp.Core.Hosting;

public interface IEnvironmentValidator
{
    (bool success, string error) VerifyDependencies();
}