using JiggleSharp.Core.Hosting;

namespace JiggleSharp.Windows;

/// <summary>
/// Windows implementation of <see cref="IEnvironmentValidator"/>.
/// No external dependencies or permissions are required on Windows —
/// all platform APIs used by JiggleSharp (user32.dll) are available
/// on all supported Windows versions without elevation.
/// </summary>
public class WindowsEnvironmentValidator : IEnvironmentValidator
{
    /// <inheritdoc/>
    public (bool success, string error) VerifyDependencies() => (true, string.Empty);
}