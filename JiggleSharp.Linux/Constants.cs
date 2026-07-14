namespace JiggleSharp.Linux;

public static class Constants
{
    public static string SessionTypeNotWaylandMessage =
        "JiggleSharp's Linux mouse movement requires a Wayland session. X11 sessions are not currently supported.";

    public static string PortalServiceNotAvailableMessage =
        "The xdg-desktop-portal service (org.freedesktop.portal.Desktop) was not found on the D-Bus session bus. " +
        "Please make sure xdg-desktop-portal and a RemoteDesktop backend (e.g. xdg-desktop-portal-gnome or " +
        "xdg-desktop-portal-kde) are installed and running.";
}