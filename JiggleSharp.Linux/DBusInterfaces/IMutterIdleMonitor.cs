using Tmds.DBus;

namespace JiggleSharp.Linux.DBusInterfaces;

/// <summary>
/// Minimal Tmds.DBus projection of the <c>org.gnome.Mutter.IdleMonitor</c>
/// D-Bus interface exposed by GNOME Shell / Mutter at
/// <c>/org/gnome/Mutter/IdleMonitor/Core</c>.
///
/// Only the subset of methods required by this project are declared here.
/// The interface is an internal GNOME API — it is not part of a published
/// freedesktop.org spec — so the authoritative reference is the Mutter source:
/// https://gitlab.gnome.org/GNOME/mutter/-/blob/main/src/org.gnome.Mutter.IdleMonitor.xml
/// </summary>
[DBusInterface("org.gnome.Mutter.IdleMonitor")]
public interface IMutterIdleMonitor : IDBusObject
{
    /// <summary>
    /// Returns the number of milliseconds that have elapsed since the last
    /// user input event was received across all input devices.
    ///
    /// The D-Bus member name is <c>GetIdletime</c> (Tmds.DBus appends the
    /// <c>Async</c> suffix by convention). The return type on the wire is
    /// <c>uint64</c> (D-Bus type code <c>t</c>).
    ///
    /// Callers should clamp the result to a reasonable maximum before use;
    /// see <c>MutterIdleTimeProvider.MaxIdleTime</c>.
    /// </summary>
    Task<ulong> GetIdletimeAsync();
}