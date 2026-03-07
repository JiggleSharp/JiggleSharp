using Tmds.DBus;

namespace JiggleSharp.Linux.DBusInterfaces;

/// <summary>
/// Minimal Tmds.DBus projection of the <c>org.freedesktop.DBus</c> interface
/// exposed by the D-Bus daemon itself at <c>/org/freedesktop/DBus</c>.
///
/// Only the subset of methods required by this project are declared here.
/// The full specification is available at:
/// https://dbus.freedesktop.org/doc/dbus-specification.html#message-bus-messages
/// </summary>
[DBusInterface("org.freedesktop.DBus")]
public interface IOrgFreedesktopDBus : IDBusObject
{
    /// <summary>
    /// Returns <c>true</c> if the well-known bus name <paramref name="name"/>
    /// currently has an owner on the session bus.
    ///
    /// This is the canonical way to check whether a D-Bus service is running
    /// without activating it (unlike <c>StartServiceByName</c>).
    /// Used internally to verify that <c>org.gnome.Mutter.IdleMonitor</c> is
    /// present before attempting to create a proxy.
    /// </summary>
    Task<bool> NameHasOwnerAsync(string name);
}