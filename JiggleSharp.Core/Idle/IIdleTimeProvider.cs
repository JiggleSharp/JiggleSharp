namespace JiggleSharp.Core.Idle;

/// <summary>
/// Provides an interface for JiggleSharp to retrieve the system idle time
/// </summary>
public interface IIdleTimeProvider
{
    Task<TimeSpan> GetIdleTimeAsync(CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    event EventHandler<IdleTimeChangedEventArgs>? IdleTimeChanged;
    void Start();
    Task StopAsync();
}

/// <summary>
/// Raised when the system idle time has changed
/// </summary>
/// <param name="IdleTime">The new system idle time</param>
public record IdleTimeChangedEventArgs (TimeSpan IdleTime);