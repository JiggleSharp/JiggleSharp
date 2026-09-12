using System.IO.Pipes;
using System.Text;

namespace JiggleSharp.Core.Ipc;

/// <summary>
/// Sends a single command to the JiggleSharp instance already running on this
/// machine, if there is one.
///
/// <para>
/// Used by a freshly launched process handling a command-line flag such as
/// <c>--start</c>. Because the new process has its own empty in-memory state,
/// the only way to affect the tray instance is to talk to it over the pipe.
/// </para>
///
/// <para>
/// Deliberately does not log: it runs before Serilog is configured, in a
/// short-lived process whose entire job is one request/response round trip.
/// </para>
/// </summary>
public static class IpcClient
{
    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>
    /// Milliseconds to wait for the round trip before concluding that no
    /// instance is running.
    ///
    /// <para>
    /// This value is user-visible latency: when nothing is listening, .NET does
    /// not fail fast with a "connection refused" — it waits out the full timeout
    /// and then throws <see cref="TimeoutException"/>. So this is roughly how
    /// long <c>--stop</c> takes to print "not running", and it needs to stay
    /// short enough to feel instant while tolerating a momentarily busy pipe.
    /// </para>
    /// </summary>
    public const int DefaultTimeoutMilliseconds = 500;

    /// <summary>Stream buffer size for the request and response readers/writers.</summary>
    private const int BufferSize = 1024;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Connects to the running instance, sends one command, and returns its
    /// response.
    /// </summary>
    /// <param name="command">A command constant from <see cref="IpcProtocol"/>.</param>
    /// <param name="timeoutMilliseconds">
    /// Total budget for connecting and completing the round trip.
    /// Defaults to <see cref="DefaultTimeoutMilliseconds"/>.
    /// </param>
    /// <returns>
    /// The response line, or <c>null</c> if no instance is running or it failed
    /// to answer within the timeout. Callers treat <c>null</c> as "not running":
    /// an instance that has wedged badly enough to miss the deadline cannot be
    /// remote-controlled anyway.
    /// </returns>
    public static async Task<string?> SendAsync(
        string command,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        try
        {
            using var timeout = new CancellationTokenSource(timeoutMilliseconds);
            using var pipe    = new NamedPipeClientStream(
                ".",
                IpcProtocol.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.ConnectAsync(timeoutMilliseconds, timeout.Token);

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            await using var writer = new StreamWriter(pipe, encoding, BufferSize, leaveOpen: true) { AutoFlush = true };
            using var       reader = new StreamReader(pipe, encoding, detectEncodingFromByteOrderMarks: false, BufferSize, leaveOpen: true);

            await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
            await writer.FlushAsync(timeout.Token);

            return await reader.ReadLineAsync(timeout.Token);
        }
        catch (TimeoutException)
        {
            // Nothing listening on the pipe — no instance is running.
            return null;
        }
        catch (OperationCanceledException)
        {
            // Connected, but the instance did not answer in time.
            return null;
        }
        catch (IOException)
        {
            // Pipe broke mid-exchange (instance quit during the round trip).
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // A pipe of this name exists but belongs to another user.
            return null;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if another JiggleSharp instance is running and
    /// answering on the pipe. Implemented as a <see cref="IpcProtocol.Status"/>
    /// round trip — any response at all proves a live instance.
    /// </summary>
    public static async Task<bool> IsInstanceRunningAsync(
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        => await SendAsync(IpcProtocol.Status, timeoutMilliseconds) is not null;
}
