using JiggleSharp.Core.Idle;
using Tmds.DBus;

namespace JiggleSharp.Linux.Idle;

public class MutterIdleTimeProvider : IIdleTimeProvider, IDisposable
{
    private readonly MutterIdleMonitorDbus _dbus = new();

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

            return TimeSpan.FromMilliseconds(idleMs);
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

    public void Dispose()
    {
        _dbus.Dispose();
    }
}