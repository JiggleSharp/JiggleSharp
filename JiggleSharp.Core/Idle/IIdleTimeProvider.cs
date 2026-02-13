namespace JiggleSharp.Core.Idle;

/// <summary>
/// Provides an interface for JiggleSharp to retrieve the system idle time
/// </summary>
public interface IIdleTimeProvider
{
    Task<TimeSpan> GetIdleTimeAsync(CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}