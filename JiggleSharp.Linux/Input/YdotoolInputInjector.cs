using JiggleSharp.Core.Input;

namespace JiggleSharp.Linux.Input;

public class YdotoolInputInjector : IInputInjector
{
    public Task MoveMouseAsync(int dx, int dy, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}