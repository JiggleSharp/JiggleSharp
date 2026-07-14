using JiggleSharp.Core.Hosting;
using JiggleSharp.Linux.DBusInterfaces;
using Serilog;
using Tmds.DBus;

namespace JiggleSharp.Linux;

/// <summary>
/// Validates that the Linux-specific runtime dependencies required by
/// JiggleSharp are satisfied on the current machine.
///
/// Currently checks:
///   - The session is running under Wayland (required for the RemoteDesktop
///     portal's input-injection backends).
///   - The <c>org.freedesktop.portal.Desktop</c> service is reachable on the
///     D-Bus session bus (i.e. xdg-desktop-portal is installed and running).
/// </summary>
public class LinuxEnvironmentValidator : IEnvironmentValidator
{
    private const string PortalBusName = "org.freedesktop.portal.Desktop";

    /// <summary>
    /// Checks that all Linux runtime dependencies are present and operational.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the session is Wayland and the desktop portal service is
    /// reachable; <c>false</c> otherwise, along with a combined error message.
    /// </returns>
    public (bool success, string error) VerifyDependencies()
    {
        var errorMessage = new List<string>();

        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        var isWayland = string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
        if (!isWayland)
            errorMessage.Add(Constants.SessionTypeNotWaylandMessage);

        var portalAvailable = Task.Run(IsPortalAvailableAsync).GetAwaiter().GetResult();
        if (!portalAvailable)
            errorMessage.Add(Constants.PortalServiceNotAvailableMessage);

        var combinedMessages = string.Join(Environment.NewLine, errorMessage);

        if (errorMessage.Any())
            Log.Error(combinedMessages);

        return (isWayland && portalAvailable, combinedMessages);
    }

    /// <summary>
    /// Checks whether <c>org.freedesktop.portal.Desktop</c> currently has an
    /// owner on the session bus, using a short-lived connection that is
    /// closed immediately after the check.
    /// </summary>
    private static async Task<bool> IsPortalAvailableAsync()
    {
        try
        {
            using var connection = new Connection(Address.Session);
            await connection.ConnectAsync().ConfigureAwait(false);

            var dbus = connection.CreateProxy<IOrgFreedesktopDBus>("org.freedesktop.DBus", "/org/freedesktop/DBus");
            return await dbus.NameHasOwnerAsync(PortalBusName).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }
}
