using System.IO.Pipes;
using System.Text;
using Serilog;

namespace JiggleSharp.Core.Ipc;

/// <summary>
/// Named-pipe listener that lets a freshly launched JiggleSharp process control
/// the jiggle engine inside the already-running tray instance.
///
/// <para>
/// The server also acts as the application's single-instance lock. The pipe is
/// created with a maximum of one server instance, so a second process attempting
/// to claim the same name fails immediately — on every supported platform — and
/// <see cref="TryStart"/> reports that failure rather than throwing.
/// </para>
///
/// <para>
/// A single <see cref="NamedPipeServerStream"/> is created up front and reused
/// for the life of the server: after each client disconnects the same instance
/// is recycled via <see cref="NamedPipeServerStream.Disconnect"/>. Holding one
/// instance open continuously is what keeps the name claimed, so the lock is
/// never briefly released between connections. Commands are instantaneous, so
/// serving one client at a time costs nothing.
/// </para>
///
/// <para>
/// Platform notes: .NET implements named pipes as Unix domain sockets on macOS
/// and Linux. The socket file is unlinked when the stream is disposed, and a
/// stale file left behind by a crash does not prevent a later bind.
/// <see cref="PipeOptions.CurrentUserOnly"/> restricts access to the current
/// user — on Windows via the pipe ACL, on Unix by verifying the connecting
/// peer's user id (the socket file itself remains world-readable).
/// </para>
/// </summary>
public sealed class IpcServer : IDisposable
{
    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>Stream buffer size for the request and response readers/writers.</summary>
    private const int BufferSize = 1024;

    // =========================================================================
    // Fields
    // =========================================================================

    /// <summary>
    /// Handles one command line and produces the response line to send back.
    /// Supplied by the host application, which owns the jiggle engine.
    /// </summary>
    private readonly Func<string, Task<string>> _commandHandler;

    /// <summary>The long-lived pipe instance that both listens and holds the single-instance lock.</summary>
    private readonly NamedPipeServerStream _pipe;

    /// <summary>Signals the accept loop to stop when the server is disposed.</summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>The accept loop task; retained so <see cref="Dispose"/> can observe completion.</summary>
    private Task? _acceptLoop;

    /// <summary>Guards against double disposal.</summary>
    private bool _disposed;

    // =========================================================================
    // Construction
    // =========================================================================

    private IpcServer(NamedPipeServerStream pipe, Func<string, Task<string>> commandHandler)
    {
        _pipe           = pipe;
        _commandHandler = commandHandler;
    }

    /// <summary>
    /// Attempts to claim the JiggleSharp pipe name and begin listening.
    /// </summary>
    /// <param name="commandHandler">
    /// Invoked for each received command line; returns the response line to send.
    /// Exceptions thrown by the handler are converted into an
    /// <see cref="IpcProtocol.ErrorPrefix"/> response rather than propagating.
    /// </param>
    /// <param name="server">The started server on success; <c>null</c> otherwise.</param>
    /// <param name="error">A human-readable reason on failure; <c>null</c> on success.</param>
    /// <returns>
    /// <c>true</c> if the pipe was claimed and the accept loop started.
    /// <c>false</c> if another instance already holds the pipe, or the pipe could
    /// not be created — in which case the caller should continue running without
    /// remote-control support rather than treating this as fatal.
    /// </returns>
    public static bool TryStart(
        Func<string, Task<string>> commandHandler,
        out IpcServer? server,
        out string? error)
    {
        try
        {
            var pipe = new NamedPipeServerStream(
                IpcProtocol.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            var started        = new IpcServer(pipe, commandHandler);
            started._acceptLoop = Task.Run(() => started.AcceptLoopAsync(started._cts.Token));

            server = started;
            error  = null;

            Log.Information("IPC server listening on pipe {PipeName}.", IpcProtocol.PipeName);
            return true;
        }
        catch (Exception ex)
        {
            server = null;
            error  = ex.Message;

            Log.Warning(ex, "Could not claim IPC pipe {PipeName}: {Message}",
                IpcProtocol.PipeName, ex.Message);
            return false;
        }
    }

    // =========================================================================
    // Accept loop
    // =========================================================================

    /// <summary>
    /// Accepts connections until cancelled, servicing exactly one request per
    /// connection. A failure while servicing one client is logged and the loop
    /// continues, so a malformed request can never take down remote control for
    /// the rest of the session.
    /// </summary>
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _pipe.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "IPC server stopped accepting connections: {Message}", ex.Message);
                break;
            }

            try
            {
                await ServeConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to service an IPC request: {Message}", ex.Message);
            }
            finally
            {
                // Recycle the instance so the next client can connect without
                // the pipe name ever being released.
                try
                {
                    if (_pipe.IsConnected)
                        _pipe.Disconnect();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Ignoring error while disconnecting an IPC client: {Message}", ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Reads one command line from the connected client, dispatches it to the
    /// handler, and writes the response line back.
    /// </summary>
    private async Task ServeConnectionAsync(CancellationToken ct)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using var writer = new StreamWriter(_pipe, encoding, BufferSize, leaveOpen: true) { AutoFlush = true };
        using var       reader = new StreamReader(_pipe, encoding, detectEncodingFromByteOrderMarks: false, BufferSize, leaveOpen: true);

        var request = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(request))
            return;

        string response;
        try
        {
            response = await _commandHandler(request.Trim());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "IPC command handler threw for request {Request}: {Message}", request, ex.Message);
            response = IpcProtocol.Error(ex.Message);
        }

        Log.Debug("IPC request {Request} -> {Response}", request.Trim(), response);

        await writer.WriteLineAsync(response.AsMemory(), ct);
        await writer.FlushAsync(ct);
    }

    // =========================================================================
    // Disposal
    // =========================================================================

    /// <summary>
    /// Stops the accept loop and releases the pipe, freeing the single-instance
    /// lock. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _cts.Cancel();
            _pipe.Dispose();

            // The loop observes cancellation through the pipe; give it a moment
            // to unwind so shutdown logging stays ordered.
            _acceptLoop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Ignoring error while shutting down the IPC server: {Message}", ex.Message);
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
