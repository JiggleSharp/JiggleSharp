using Tmds.DBus;

namespace JiggleSharp.Linux.DBusInterfaces;

[DBusInterface("org.freedesktop.DBus")]
public interface IOrgFreedesktopDBus : IDBusObject
{
    Task<bool> NameHasOwnerAsync(string name);
}