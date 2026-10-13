namespace XivDesktop.Core.Notifications;

public sealed record AgentNotification(long Sequence, string Source, long Nonce, uint Id, string Kind,
    string App, string DesktopEntry, string Summary, string Body, byte Urgency);

public sealed record AppNotificationSnapshot(long Revision, IReadOnlyList<AgentNotification> Events);

/// <summary>New events only: enabling, reconnecting or losing IPC establishes a fresh baseline.</summary>
public sealed class NotificationInbox
{
    private long? revision;
    private bool wasEnabled;

    public IReadOnlyList<AgentNotification> Observe(AppNotificationSnapshot? snapshot, bool enabled)
    {
        if (snapshot is null) { revision = null; wasEnabled = false; return []; }
        var firstEnabled = enabled && !wasEnabled;
        wasEnabled = enabled;
        var previous = revision;
        revision = snapshot.Revision;
        if (!enabled || firstEnabled || previous is null || snapshot.Revision <= previous) return [];
        return snapshot.Events.Where(e => e.Sequence > previous.Value).ToArray();
    }
}
