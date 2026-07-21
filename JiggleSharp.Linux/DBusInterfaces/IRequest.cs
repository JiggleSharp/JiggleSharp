using Tmds.DBus;

namespace JiggleSharp.Linux.DBusInterfaces;

/// <summary>
/// Minimal Tmds.DBus projection of the <c>org.freedesktop.portal.Request</c>
/// interface. Every portal method that requires user interaction returns an
/// object path implementing this interface; the actual result is delivered
/// asynchronously via the <c>Response</c> signal on that object.
///
/// Spec: https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Request.html
/// </summary>
[DBusInterface("org.freedesktop.portal.Request")]
public interface IRequest : IDBusObject
{
    /// <summary>
    /// Fired once when the request completes. <c>response</c> is 0 for
    /// success, 1 if the user cancelled/denied, or 2 for any other
    /// termination. <c>results</c> holds method-specific result data.
    /// </summary>
    Task<IDisposable> WatchResponseAsync(Action<(uint Response, IDictionary<string, object> Results)> handler);
}
