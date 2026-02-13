namespace JiggleSharp.Core.Engine;

/// <summary>
/// The status of the engine
/// </summary>
public enum EngineStatus
{
    /// <summary>
    /// Specifies that the system has not been idle long enough to trigger a movement
    /// </summary>
    Safe,
    /// <summary>
    /// Specifies that JiggleSharp is preparing to move the mouse
    /// </summary>
    Warning,
    /// <summary>
    /// Specifies that JiggleSharp is in the process of moving the mouse
    /// </summary>
    Acting
}