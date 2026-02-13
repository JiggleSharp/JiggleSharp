namespace JiggleSharp.Core.Hosting;

/// <summary>
/// Provides an interface for a platform to accept JiggleSharp's logging information
/// </summary>
public interface ILogger
{
    void Info(string message);
    void Error(string message, Exception? ex = null);
    void Warning(string message);
}