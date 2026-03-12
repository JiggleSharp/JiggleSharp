using System.Runtime.InteropServices;
using JiggleSharp.Core.Idle;
using Serilog;

namespace JiggleSharp.Windows.Idle;

/// <summary>
/// Windows implementation of <see cref="IIdleTimeProvider"/> using <c>GetLastInputInfo</c>
/// from user32.dll to determine the time elapsed since the last user input event.
/// </summary>
public class WindowsIdleTimeProvider(TimeSpan? pollInterval = null) : IIdleTimeProvider, IAsyncDisposable
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private TimeSpan _lastReported = TimeSpan.MinValue;

    // -------------------------------------------------------------------------
    // IIdleTimeProvider
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public event EventHandler<IdleTimeChangedEventArgs>? IdleTimeChanged;

    /// <inheritdoc/>
    /// <remarks>
    /// Uses <c>GetLastInputInfo</c> to retrieve the tick count of the last input
    /// event, then diffs against <see cref="Environment.TickCount64"/> to compute
    /// elapsed idle time. <c>dwTime</c> is a 32-bit tick count (wraps every ~49 days);
    /// clamped to zero to guard against edge cases near wraparound.
    /// </remarks>
    public Task<TimeSpan> GetIdleTimeAsync(CancellationToken ct = default)
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };

        if (!GetLastInputInfo(ref info))
            throw new InvalidOperationException($"GetLastInputInfo failed: {Marshal.GetLastWin32Error()}");

        long idleMs = Environment.TickCount64 - (long)info.dwTime;
        return Task.FromResult(TimeSpan.FromMilliseconds(Math.Max(0, idleMs)));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Performs a live probe of <c>GetLastInputInfo</c>. This call is available on
    /// all supported Windows versions and requires no elevated permissions.
    /// </remarks>
    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return Task.FromResult(GetLastInputInfo(ref info));
    }

    /// <inheritdoc/>
    public void Start()
    {
        if (_pollTask is { IsCompleted: false }) return;

        _cts = new CancellationTokenSource();
        _pollTask = PollAsync(_cts.Token);
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (_cts is null) return;

        await _cts.CancelAsync();

        if (_pollTask is not null)
            await _pollTask.ConfigureAwait(false);

        _cts.Dispose();
        _cts = null;
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync();

    // -------------------------------------------------------------------------
    // Poll loop
    // -------------------------------------------------------------------------

    /// <summary>
    /// Polls <see cref="GetIdleTimeAsync"/> at <see cref="_pollInterval"/> and raises
    /// <see cref="IdleTimeChanged"/> whenever the reported idle time changes.
    /// Errors are logged but do not terminate the loop.
    /// </summary>
    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct);

                var idle = await GetIdleTimeAsync(ct);
                if (idle == _lastReported) continue;

                _lastReported = idle;
                IdleTimeChanged?.Invoke(this, new IdleTimeChangedEventArgs(idle));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Error(ex, "[WindowsIdleTimeProvider] Poll error");
            }
        }
    }

    // -------------------------------------------------------------------------
    // P/Invoke
    // -------------------------------------------------------------------------

    /// <summary>
    /// Contains the time of the last input event in milliseconds, sharing the
    /// same epoch as <c>GetTickCount</c>. 32-bit value; wraps every ~49 days.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}