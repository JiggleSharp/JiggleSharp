using JiggleSharp.Core.Engine;
using JiggleSharp.Core.Hosting;
using JiggleSharp.Core.Idle;
using JiggleSharp.Core.Input;
using JiggleSharp.Mac.Idle;
using JiggleSharp.Mac.Input;

namespace JiggleSharp.Mac;

public class MacPlatformServices : IPlatformServices
{
    public IIdleTimeProvider IdleTimeProvider => new MacIdleTimeProvider();
    public IInputInjector InputInjector => new MacInputInjector();
    public IEnvironmentValidator EnvironmentValidator => new MacEnvironmentValidator();
}