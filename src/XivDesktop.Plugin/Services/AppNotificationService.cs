using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using XivDesktop.Core;
using XivDesktop.Core.Notifications;
using XivDesktop.Core.Windows;

namespace XivDesktop.Plugin.Services;

/// <summary>Only the private compositor bus is read. Privacy changes also dismiss existing toasts.</summary>
public sealed class AppNotificationService : IDisposable, IAppNotificationSink
{
    private readonly Configuration config;
    private readonly GhosttyCallBackend backend;
    private readonly SessionService session;
    private readonly DesktopService desktop;
    private readonly IFramework framework;
    private readonly INotificationManager notifications;
    private readonly AppNotificationController controller;
    private long nextPoll;
    private bool disposed;

    private sealed class Toast(IActiveNotification handle, INotificationIcon? icon) : IAppNotificationToast
    {
        private bool dismissed;
        public bool IsDismissed => dismissed || handle.DismissReason != null;
        public void HideBody() => handle.Content = "";
        public void Dismiss()
        {
            if (dismissed) return;
            dismissed = true;
            // Dalamud fades dismissed notifications: scrub before starting that fade.
            handle.Title = "App notification";
            handle.Content = "";
            handle.MinimizedText = "";
            handle.Icon = null;
            handle.DismissNow();
            (icon as IDisposable)?.Dispose();
        }
    }

    public AppNotificationService(Configuration config, GhosttyCallBackend backend, SessionService session,
        DesktopService desktop, IFramework framework, INotificationManager notifications)
    {
        this.config = config;
        this.backend = backend;
        this.session = session;
        this.desktop = desktop;
        this.framework = framework;
        this.notifications = notifications;
        controller = new(this);
        framework.Update += Tick;
    }

    private static string Route(string route) => NotificationTargets.Route(route);
    private static string AppId(string id) => NotificationTargets.AppId(id);
    private AppInfo? App(AgentNotification message) => message.DesktopEntry.Length == 0 ? null
        : desktop.Catalog.Catalog.Find(AppId(message.DesktopEntry));
    private IReadOnlyList<WindowPanel> Panels(AgentNotification message) => NotificationTargets.Find(message,
        session.OpenPanels(), w => session.NotificationAppFor(w)?.Id);

    public bool IsSuppressed(AgentNotification message)
    {
        if (!config.AppNotificationsEnabled || session.Veiled || session.Stowed) return true;
        if (message.DesktopEntry.Length > 0 && UserLists.Contains(config.PrivateApps, AppId(message.DesktopEntry))) return true;
        return session.OpenPanels().Any(w => Route(w.Agent) == Route(message.Source) && session.IsPrivate(w)
            && (message.DesktopEntry.Length == 0 || session.AppFor(w) is not { } app
                || string.Equals(app.Id, AppId(message.DesktopEntry), StringComparison.OrdinalIgnoreCase)));
    }

    private void Tick(IFramework _)
    {
        try { Poll(); }
        catch
        {
            // Do not log app-supplied notification text (including exception messages).
            controller.Reset();
        }
    }

    private void Poll()
    {
        if (disposed) return;
        controller.Refresh(config.AppNotificationsEnabled, config.AppNotificationsTitleOnly);
        var now = Environment.TickCount64;
        if (now < nextPoll) return;
        nextPoll = now + 500;
        controller.Observe(backend.ReadNotifications(), config.AppNotificationsEnabled, config.AppNotificationsTitleOnly, now);
    }

    public IAppNotificationToast Show(AgentNotification message, NotificationPresentation presentation, Action click)
    {
        var icon = App(message)?.IconPath is { } path ? INotificationIcon.FromFile(path) : null;
        try
        {
            var handle = notifications.AddNotification(new Notification
            {
                Title = presentation.Title, Content = presentation.Body, Icon = icon,
                Type = NotificationType.Info, InitialDuration = TimeSpan.FromSeconds(6),
                HardExpiry = DateTime.Now.AddSeconds(10),
            });
            handle.Click += _ => click();
            return new Toast(handle, icon);
        }
        catch { (icon as IDisposable)?.Dispose(); throw; }
    }

    public void Focus(AgentNotification message)
    {
        var panels = Panels(message);
        var panel = panels.FirstOrDefault(w => w.Focused) ?? panels.LastOrDefault();
        if (panel != null) session.Focus(panel.Id);
    }

    public void Dispose()
    {
        disposed = true;
        framework.Update -= Tick;
        controller.Dispose();
    }
}
