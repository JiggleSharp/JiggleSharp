using JiggleSharp.Core.Input;

namespace JiggleSharp.Mac.Input;

public class MacInputInjector : IInputInjector
{
    public Task MoveMouseAsync(int dx, int dy, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}