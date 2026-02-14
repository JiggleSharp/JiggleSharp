using JiggleSharp.Core.Idle;
using Tmds.DBus;

namespace JiggleSharp.Linux.Idle;

public class MutterIdleTimeProvider : IIdleTimeProvider, IDisposable
{
    private readonly MutterIdleMonitorDbus _dbus = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            _ = await _dbus.GetProxyAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public event EventHandler<IdleTimeChangedEventArgs>? IdleTimeChanged;

    public async Task<TimeSpan> GetIdleTimeAsync(CancellationToken ct = default)
    {
        try
        {
            var proxy = await _dbus.GetProxyAsync().ConfigureAwait(false);

            // Mutter returns uint64. In practice this is treated as milliseconds by common callers.
            // Example tooling prints milliseconds for GetIdletime. :contentReference[oaicite:1]{index=1}
            ulong idleMs = await proxy.GetIdletimeAsync().ConfigureAwait(false);

            // Clamp to something sane in case of weirdness.
            if (idleMs > (ulong)TimeSpan.FromDays(3650).TotalMilliseconds)
                idleMs = (ulong)TimeSpan.FromDays(3650).TotalMilliseconds;

            var idleTime = TimeSpan.FromMilliseconds(idleMs);
            
            IdleTimeChanged?.Invoke(this, new IdleTimeChangedEventArgs(idleTime));

            return idleTime;
        }
        catch (DBusException ex) when (
            ex.ErrorName == "org.freedesktop.DBus.Error.ServiceUnknown" ||
            ex.ErrorName == "org.freedesktop.DBus.Error.NameHasNoOwner" ||
            ex.ErrorName == "org.freedesktop.DBus.Error.AccessDenied")
        {
            // Not GNOME / not available / sandbox denies.
            return TimeSpan.Zero;
        }
    }
    
    public void Start()
    {
        if (_loop != null) return;

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;

        await _cts.CancelAsync();
        try
        {
            if (_loop != null) 
                await _loop;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _loop = null;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task LoopAsync(CancellationToken ct = default)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(ct))
        {
            _ = await GetIdleTimeAsync(ct);
        }
    }

    public void Dispose()
    {
        _dbus.Dispose();
    }
}