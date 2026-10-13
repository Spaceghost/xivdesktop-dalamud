using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Notifications;

public static class NotificationTargets
{
    public static string Route(string route) => route.Length == 0 || route == "default" ? "local" : route;
    public static string AppId(string id) => id.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? id : id + ".desktop";

    public static IReadOnlyList<WindowPanel> Find(AgentNotification message, IEnumerable<WindowPanel> panels,
        Func<WindowPanel, string?> appId)
    {
        if (message.DesktopEntry.Length == 0) return [];
        return panels.Where(w => !w.IsEnded && Route(w.Agent) == Route(message.Source)
            && string.Equals(appId(w), AppId(message.DesktopEntry), StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
