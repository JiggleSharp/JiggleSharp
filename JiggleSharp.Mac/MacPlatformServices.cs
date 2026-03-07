using JiggleSharp.Core.Engine;
using JiggleSharp.Core.Hosting;
using JiggleSharp.Core.Idle;
using JiggleSharp.Core.Input;
using JiggleSharp.Mac.Idle;
using JiggleSharp.Mac.Input;
using JiggleSharp.Mac.System;

namespace JiggleSharp.Mac;

public class MacPlatformServices : IPlatformServices
{
    public IIdleTimeProvider IdleTimeProvider { get; } = new MacIdleTimeProvider();
    public IInputInjector InputInjector { get; } = new MacInputInjector();
    public IEnvironmentValidator EnvironmentValidator { get; } = new MacEnvironmentValidator();
    public ISystemIntegrationHandler SystemIntegrationHandler { get; } = new MacSystemIntegrationHandler();
}