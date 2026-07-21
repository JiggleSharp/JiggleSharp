namespace JiggleSharp.Core.Input;

/// <summary>
/// Provides an interface used by JiggleSharp to perform a mouse movement independent of platform
/// </summary>
public interface IInputInjector
{
    Task MoveMouseAsync(int dx, int dy, CancellationToken ct);
    event EventHandler<Exception> InputInjectorFailure;

    /// <summary>
    /// Proactively ensures the injector is ready to move the mouse — e.g. by
    /// completing a one-time OS consent/permission flow up front — instead
    /// of leaving that to the first <see cref="MoveMouseAsync"/> call. Called
    /// when the jiggle engine starts. Platforms with nothing to prepare in
    /// advance can rely on the default no-op implementation.
    /// </summary>
    Task RequestPermissionAsync(CancellationToken ct) => Task.CompletedTask;
}