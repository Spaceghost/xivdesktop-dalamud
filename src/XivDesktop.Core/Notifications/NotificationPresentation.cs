namespace XivDesktop.Core.Notifications;

public sealed record NotificationPresentation(string Title, string Body);

/// <summary>Privacy is checked before rate accounting. A burst cannot create more than four toasts in ten seconds.</summary>
public sealed class NotificationPresentationPolicy
{
    private readonly Queue<long> shown = new();

    public NotificationPresentation? Present(AgentNotification message, long now, bool enabled,
        bool suppressed, bool titleOnly)
    {
        if (!enabled || suppressed || message.Kind != "notification") return null;
        while (shown.TryPeek(out var previous) && (now < previous || now - previous >= 10_000)) shown.Dequeue();
        if (shown.Count >= 4) return null;
        shown.Enqueue(now);
        return new(message.Summary.Length > 0 ? message.Summary : message.App,
            titleOnly ? "" : message.Body);
    }
}
