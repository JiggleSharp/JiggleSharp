using Tmds.DBus;

namespace JiggleSharp.Linux.DBusInterfaces;

/// <summary>
/// Minimal Tmds.DBus projection of the <c>org.freedesktop.portal.RemoteDesktop</c>
/// interface exposed by xdg-desktop-portal at
/// <c>/org/freedesktop/portal/desktop</c>.
///
/// Only the members required for pointer-only remote control are declared.
/// Spec: https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.RemoteDesktop.html
/// </summary>
[DBusInterface("org.freedesktop.portal.RemoteDesktop")]
public interface IRemoteDesktop : IDBusObject
{
    /// <summary>
    /// Creates a remote desktop session. Returns a request object path; the
    /// result (including the session handle) arrives via that request's
    /// <c>Response</c> signal.
    /// </summary>
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);

    /// <summary>
    /// Selects which input device types (see the portal's device type
    /// bitmask: keyboard = 1, pointer = 2, touchscreen = 4) should be
    /// requested for <paramref name="sessionHandle"/>.
    /// </summary>
    Task<ObjectPath> SelectDevicesAsync(ObjectPath sessionHandle, IDictionary<string, object> options);

    /// <summary>
    /// Starts the session, prompting the user with a one-time consent dialog
    /// for the devices selected via <see cref="SelectDevicesAsync"/>.
    /// </summary>
    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);

    /// <summary>
    /// Notifies the compositor of a relative pointer motion. Unlike the
    /// other members here, this is a plain method call — it does not create
    /// a request object or emit a <c>Response</c> signal.
    /// </summary>
    Task NotifyPointerMotionAsync(ObjectPath sessionHandle, IDictionary<string, object> options, double dx, double dy);
}
