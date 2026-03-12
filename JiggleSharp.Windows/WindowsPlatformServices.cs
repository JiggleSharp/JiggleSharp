using JiggleSharp.Core.Engine;
using JiggleSharp.Core.Hosting;
using JiggleSharp.Core.Idle;
using JiggleSharp.Core.Input;
using JiggleSharp.Windows.Idle;
using JiggleSharp.Windows.Input;
using JiggleSharp.Windows.System;

namespace JiggleSharp.Windows;

/// <summary>
/// Composes and exposes all Windows-specific platform service implementations
/// for consumption by the JiggleSharp core engine via <see cref="IPlatformServices"/>.
/// </summary>
public class WindowsPlatformServices : IPlatformServices
{
    /// <inheritdoc/>
    public IIdleTimeProvider IdleTimeProvider { get; } = new WindowsIdleTimeProvider();

    /// <inheritdoc/>
    public IInputInjector InputInjector { get; } = new WindowsInputInjector();

    /// <inheritdoc/>
    public IEnvironmentValidator EnvironmentValidator { get; } = new WindowsEnvironmentValidator();

    /// <inheritdoc/>
    public ISystemIntegrationHandler SystemIntegrationHandler { get; } = new WindowsSystemIntegrationHandler();
}