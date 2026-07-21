using Tmds.DBus;

namespace JiggleSharp.Linux.DBusInterfaces;

/// <summary>
/// Minimal Tmds.DBus projection of the <c>org.freedesktop.portal.Session</c>
/// interface. Returned (indirectly, via its object path) by
/// <c>RemoteDesktop.CreateSession</c>; closing it ends the remote control
/// grant and any associated consent.
///
/// Spec: https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Session.html
/// </summary>
[DBusInterface("org.freedesktop.portal.Session")]
public interface ISession : IDBusObject
{
    Task CloseAsync();
}
