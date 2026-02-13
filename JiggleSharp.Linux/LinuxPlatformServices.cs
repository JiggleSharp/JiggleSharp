using JiggleSharp.Core.Engine;
using JiggleSharp.Core.Hosting;
using JiggleSharp.Core.Idle;
using JiggleSharp.Core.Input;
using JiggleSharp.Linux.Idle;
using JiggleSharp.Linux.Input;

namespace JiggleSharp.Linux;

public class LinuxPlatformServices : IPlatformServices
{
    public IIdleTimeProvider IdleTimeProvider => new MutterIdleTimeProvider();
    public IInputInjector InputInjector => new YdotoolInputInjector();
    public IEnvironmentValidator EnvironmentValidator => new LinuxEnvironmentValidator();
}