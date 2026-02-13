using Tmds.DBus;

namespace JiggleSharp.Linux.Idle;

// The DBus interface is documented by Mutter as org.gnome.Mutter.IdleMonitor.
// Method: GetIdletime() -> uint64
[DBusInterface("org.gnome.Mutter.IdleMonitor")]
internal interface IMutterIdleMonitor : IDBusObject
{
    // Exact DBus member name is GetIdletime
    Task<ulong> GetIdletimeAsync();
}