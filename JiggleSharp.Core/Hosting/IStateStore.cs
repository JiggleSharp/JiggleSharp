namespace JiggleSharp.Core.Hosting;

/// <summary>
/// Provides an interface for platform handlers to accept updates to JiggleSharp's state
/// </summary>
public interface IStateStore
{
    void SetEmoji(string emoji);
}