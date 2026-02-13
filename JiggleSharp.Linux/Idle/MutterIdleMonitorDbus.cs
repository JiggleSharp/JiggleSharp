using JiggleSharp.Linux.DBusInterfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus;

namespace JiggleSharp.Linux.Idle;

internal sealed class MutterIdleMonitorDbus : IDisposable
{
    private const string ServiceName = "org.gnome.Mutter.IdleMonitor";
    private static readonly ObjectPath CoreObjectPath = new("/org/gnome/Mutter/IdleMonitor/Core");

    private Connection? _session;
    private IMutterIdleMonitor? _proxy;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public async Task<IMutterIdleMonitor> GetProxyAsync()
    {
        if (_proxy is not null)
            return _proxy;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            if (_proxy is not null)
                return _proxy;

            _session ??= new Connection(Address.Session);

            // Your Tmds.DBus version has no CT overload.
            await _session.ConnectAsync().ConfigureAwait(false);

            if (!await IsServiceAvailableAsync(_session, ServiceName).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"DBus service '{ServiceName}' not available on the session bus. " +
                    "Are you running GNOME Shell / Mutter?");
            }

            _proxy = _session.CreateProxy<IMutterIdleMonitor>(ServiceName, CoreObjectPath);
            return _proxy;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task<bool> IsServiceAvailableAsync(Connection connection, string name)
    {
        var dbus = connection.CreateProxy<IOrgFreedesktopDBus>(
            "org.freedesktop.DBus",
            "/org/freedesktop/DBus");

        return await dbus.NameHasOwnerAsync(name).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _proxy = null;

        _session?.Dispose();
        _session = null;

        _initLock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (!_disposed) return;
        throw new ObjectDisposedException(nameof(MutterIdleMonitorDbus));
    }
}
