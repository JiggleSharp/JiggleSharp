using JiggleSharp.Core.Engine;
using JiggleSharp.Core.Hosting;
using JiggleSharp.Core.Idle;
using JiggleSharp.Core.Input;
using JiggleSharp.Linux.Idle;
using JiggleSharp.Linux.Input;
using JiggleSharp.Linux.System;

namespace JiggleSharp.Linux;

public class LinuxPlatformServices : IPlatformServices
{
    public IIdleTimeProvider IdleTimeProvider { get; } = new MutterIdleTimeProvider();
    public IInputInjector InputInjector { get; } = new YdotoolInputInjector();
    public IEnvironmentValidator EnvironmentValidator { get; } = new LinuxEnvironmentValidator();
    public ISystemIntegrationHandler SystemIntegrationHandler { get; } = new LinuxSystemIntegrationHandler();
}