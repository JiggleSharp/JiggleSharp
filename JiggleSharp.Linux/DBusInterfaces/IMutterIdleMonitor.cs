using Tmds.DBus;

namespace JiggleSharp.Linux.DBusInterfaces;

// The DBus interface is documented by Mutter as org.gnome.Mutter.IdleMonitor.
// Method: GetIdletime() -> uint64
[DBusInterface("org.gnome.Mutter.IdleMonitor")]
public interface IMutterIdleMonitor : IDBusObject
{
    // Exact DBus member name is GetIdletime
    Task<ulong> GetIdletimeAsync();
}