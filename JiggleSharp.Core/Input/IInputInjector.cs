namespace JiggleSharp.Core.Input;

/// <summary>
/// Provides an interface used by JiggleSharp to perform a mouse movement independent of platform
/// </summary>
public interface IInputInjector
{
    Task MoveMouseAsync(int dx, int dy, CancellationToken ct);
}