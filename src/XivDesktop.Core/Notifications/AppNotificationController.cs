namespace XivDesktop.Core.Notifications;

public interface IAppNotificationToast
{
    bool IsDismissed { get; }
    void HideBody();
    /// <summary>Scrub all content before starting any dismissal animation.</summary>
    void Dismiss();
}

public interface IAppNotificationSink
{
    bool IsSuppressed(AgentNotification message);
    IAppNotificationToast Show(AgentNotification message, NotificationPresentation presentation, Action click);
    void Focus(AgentNotification message);
}

/// <summary>The production toast lifecycle, independent of Dalamud rendering and game services.</summary>
public sealed class AppNotificationController(IAppNotificationSink sink) : IDisposable
{
    private readonly NotificationInbox inbox = new();
    private readonly NotificationPresentationPolicy policy = new();
    private readonly Dictionary<(string Source, long Nonce, uint Id), Entry> active = [];
    private sealed record Entry(AgentNotification Message, IAppNotificationToast Toast);
    private AppNotificationSnapshot? snapshot;
    private bool enabled;
    private bool disposed;

    private bool Current(AgentNotification message) => snapshot?.Events.LastOrDefault(e => e.Source == message.Source)?.Nonce == message.Nonce;

    /// <summary>Runs every frame so changing privacy settings also affects existing toasts.</summary>
    public void Refresh(bool enabled, bool titleOnly)
    {
        this.enabled = enabled;
        if (!enabled) inbox.Observe(snapshot, false);
        foreach (var pair in active.ToArray())
        {
            var entry = pair.Value;
            if (!enabled || sink.IsSuppressed(entry.Message) || !Current(entry.Message) || entry.Toast.IsDismissed)
                Remove(pair.Key);
            else if (titleOnly) entry.Toast.HideBody();
        }
    }

    public void Observe(AppNotificationSnapshot? next, bool enabled, bool titleOnly, long now)
    {
        if (disposed) return;
        if (next == null || snapshot != null && next.Revision < snapshot.Revision) Clear();
        snapshot = next;
        Refresh(enabled, titleOnly);
        // Replacement bursts must show only the last state, never an already closed intermediate toast.
        var messages = inbox.Observe(next, enabled).GroupBy(e => (e.Source, e.Nonce, e.Id))
            .Select(group => group.Last()).OrderBy(e => e.Sequence);
        foreach (var message in messages)
        {
            var key = (message.Source, message.Nonce, message.Id);
            Remove(key);
            var presentation = policy.Present(message, now, enabled, sink.IsSuppressed(message) || !Current(message), titleOnly);
            if (presentation == null) continue;
            Entry? entry = null;
            var toast = sink.Show(message, presentation, () =>
            {
                if (disposed || !this.enabled || entry == null || !active.TryGetValue(key, out var current)
                    || !ReferenceEquals(current, entry) || current.Toast.IsDismissed
                    || !Current(message) || sink.IsSuppressed(message)) return;
                sink.Focus(message);
            });
            active[key] = entry = new(message, toast);
        }
    }

    public void Reset()
    {
        snapshot = null;
        inbox.Observe(null, false);
        Clear();
    }

    private void Remove((string Source, long Nonce, uint Id) key)
    {
        if (active.Remove(key, out var entry)) entry.Toast.Dismiss();
    }

    private void Clear()
    {
        foreach (var key in active.Keys.ToArray()) Remove(key);
    }

    public void Dispose()
    {
        disposed = true;
        Reset();
    }
}
