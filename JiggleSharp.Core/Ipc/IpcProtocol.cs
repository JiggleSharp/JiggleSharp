namespace JiggleSharp.Core.Ipc;

/// <summary>
/// Wire protocol shared by <see cref="IpcServer"/> and <see cref="IpcClient"/>.
///
/// <para>
/// The protocol is deliberately trivial: the client writes a single UTF-8 command
/// line, the server writes a single UTF-8 response line, and the connection is
/// closed. This keeps the channel dependency-free and inspectable by hand (for
/// example with <c>socat</c> on Linux/macOS) when diagnosing a stuck instance.
/// </para>
/// </summary>
public static class IpcProtocol
{
    // =========================================================================
    // Commands (client -> server)
    // =========================================================================

    /// <summary>Start the jiggle engine. A no-op if it is already running.</summary>
    public const string Start = "START";

    /// <summary>Stop the jiggle engine. A no-op if it is already stopped.</summary>
    public const string Stop = "STOP";

    /// <summary>Invert the engine's current running state.</summary>
    public const string Toggle = "TOGGLE";

    /// <summary>
    /// Report the engine state without changing it. Also used as the probe that
    /// answers "is another instance already running?", since any response at all
    /// proves a live server on the other end of the pipe.
    /// </summary>
    public const string Status = "STATUS";

    // =========================================================================
    // Responses (server -> client)
    // =========================================================================

    /// <summary>The engine is running (jiggling is active).</summary>
    public const string ResponseRunning = "OK RUNNING";

    /// <summary>The engine is stopped.</summary>
    public const string ResponseStopped = "OK STOPPED";

    /// <summary>Prefix identifying a failure response; the remainder is a human-readable reason.</summary>
    public const string ErrorPrefix = "ERR ";

    /// <summary>Builds an error response line for the given reason.</summary>
    public static string Error(string reason) => ErrorPrefix + reason;

    /// <summary>Maps an engine running state onto the corresponding response line.</summary>
    public static string StateResponse(bool isRunning) => isRunning ? ResponseRunning : ResponseStopped;

    // =========================================================================
    // Pipe identity
    // =========================================================================

    /// <summary>
    /// Name of the named pipe used for inter-instance communication.
    ///
    /// <para>
    /// On Windows this becomes <c>\\.\pipe\JiggleSharp-{user}</c>. On macOS and
    /// Linux .NET maps it onto a Unix domain socket at
    /// <c>{TMPDIR}/CoreFxPipe_JiggleSharp-{user}</c>.
    /// </para>
    ///
    /// <para>
    /// The user name is part of the pipe name because on Linux the temp
    /// directory is the shared <c>/tmp</c>, so two users running JiggleSharp on
    /// the same machine would otherwise contend for a single socket path.
    /// (macOS already gives each user a private <c>/var/folders/…</c> temp
    /// directory, and Windows namespaces pipes per session, but a single name
    /// across all three platforms keeps the behaviour uniform.)
    /// </para>
    /// </summary>
    public static string PipeName { get; } = BuildPipeName();

    /// <summary>
    /// Derives the pipe name from the current user name, stripped to ASCII
    /// alphanumerics so it is legal in both the Windows pipe namespace and a
    /// Unix socket file name.
    /// </summary>
    private static string BuildPipeName()
    {
        var sanitized = new string(Environment.UserName
            .Where(char.IsAsciiLetterOrDigit)
            .ToArray());

        if (sanitized.Length == 0)
            sanitized = "user";
        else if (sanitized.Length > 32)
            sanitized = sanitized[..32];

        return $"JiggleSharp-{sanitized}";
    }
}
