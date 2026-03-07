using JiggleSharp.Core.Idle;
using JiggleSharp.Core.Input;
using JiggleSharp.Core.Hosting;

namespace JiggleSharp.Core.Engine;

public interface IPlatformServices
{
    IIdleTimeProvider? IdleTimeProvider { get; }
    IInputInjector InputInjector { get; }
    IEnvironmentValidator EnvironmentValidator { get; }
    ISystemIntegrationHandler SystemIntegrationHandler { get; }
}