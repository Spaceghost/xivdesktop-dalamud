using System.Text.Json;
using XivDesktop.Core.Notifications;
using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Tests;

public class NotificationTests
{
    [Fact]
    public void FocusTargetsRequireLivePanelExactAppIdentityAndMatchingAgent()
    {
        var local = new WindowPanel { Id = 1, Agent = "default", State = "live" };
        var remote = local with { Id = 2, Agent = "remote" };
        var other = local with { Id = 3 };
        var ended = local with { Id = 4, State = "ended" };
        var panels = new[] { local, remote, other, ended };
        string? AppId(WindowPanel panel) => panel.Id == 3 ? "fixture-extra.desktop" : "fixture.desktop";
        Assert.Equal(local, Assert.Single(NotificationTargets.Find(Event(1), panels, AppId)));
        Assert.Equal(remote, Assert.Single(NotificationTargets.Find(Event(1) with { Source = "remote" }, panels, AppId)));
        Assert.Empty(NotificationTargets.Find(Event(1) with { DesktopEntry = "" }, panels, AppId));
        Assert.Empty(NotificationTargets.Find(Event(1) with { Source = "missing" }, panels, AppId));
        Assert.Equal(local, Assert.Single(NotificationTargets.Find(Event(1) with { DesktopEntry = "FIXTURE.DESKTOP" }, panels, AppId)));
    }

    [Fact]
    public void NotificationMatchingDoesNotTrustWindowTitles()
    {
        var app = new AppInfo { Id = "fixture.desktop", Name = "Fixture", Command = "fixture" };
        var panel = new WindowPanel { Id = 2, App = "unrelated", Title = "Private document - Fixture" };
        Assert.Equal(app, WindowApps.Find([app], panel));
        Assert.Null(WindowApps.Find([app], panel, allowTitleMatch: false));
        Assert.Equal(app, WindowApps.Find([app], panel, "fixture.desktop", allowTitleMatch: false));
        Assert.Equal(app, WindowApps.Find([app], panel with { App = "/usr/bin/fixture --new-window" }, allowTitleMatch: false));
    }

    [Fact]
    public void PresentationSuppressesPrivateContentAndLimitsBursts()
    {
        var policy = new NotificationPresentationPolicy();
        Assert.Null(policy.Present(Event(1), 0, false, false, false));
        Assert.Null(policy.Present(Event(1), 0, true, true, false));
        Assert.Null(policy.Present(Event(1) with { Kind = "closed" }, 0, true, false, false));
        Assert.Equal(new NotificationPresentation("Title", ""), policy.Present(Event(1), 0, true, false, true));
        for (var i = 0; i < 3; i++)
            Assert.Equal("Body", policy.Present(Event(1), 1, true, false, false)!.Body);
        Assert.Null(policy.Present(Event(1), 9_999, true, false, false));
        Assert.NotNull(policy.Present(Event(1), 10_000, true, false, false));
        Assert.Null(policy.Present(Event(1), 10_000, true, false, false));
        Assert.NotNull(policy.Present(Event(1), 10_001, true, false, false));
    }

    private static AgentNotification Event(long seq) => new(seq, "local", 10, 7, "notification", "Fixture", "fixture.desktop", "Title", "Body", 1);

    [Fact]
    public void InboxSkipsBaselineDisabledEventsAndReconnectBacklog()
    {
        var inbox = new NotificationInbox();
        Assert.Empty(inbox.Observe(new(1, [Event(1)]), true));
        Assert.Single(inbox.Observe(new(2, [Event(1), Event(2)]), true));
        Assert.Empty(inbox.Observe(new(2, [Event(2)]), true));
        Assert.Empty(inbox.Observe(new(3, [Event(3)]), false));
        Assert.Empty(inbox.Observe(new(3, [Event(3)]), true));
        Assert.Single(inbox.Observe(new(4, [Event(4)]), true));
        Assert.Empty(inbox.Observe(null, true));
        Assert.Empty(inbox.Observe(new(5, [Event(5)]), true));
        Assert.Single(inbox.Observe(new(6, [Event(6)]), true));
        Assert.Empty(inbox.Observe(new(1, [Event(1)]), true));
        Assert.Empty(inbox.Observe(new(2, [Event(2)]), false));
        Assert.Empty(inbox.Observe(new(3, [Event(3)]), true)); // created while disabled, before the first enabled poll
        Assert.Single(inbox.Observe(new(4, [Event(4)]), true));
    }

    private static string Wire(object[] events, int revision = 3) => JsonSerializer.Serialize(new { ok = true, result = new { rev = revision, events } });
    private static object Message(int sequence = 1, string body = "Synthetic body") => new
    { seq = sequence, source = "local", nonce = 10, id = 7, kind = "notification", app = "Fixture", desktop_entry = "fixture.desktop", summary = "Title", body, urgency = 1 };

    [Fact]
    public void WirePreservesRouteNonceAndCloseEvents()
    {
        var parsed = GhosttyWire.ParseNotifications(Wire([Message(), new { seq = 2, source = "remote", nonce = 20, id = 7, kind = "closed" }]))!;
        Assert.Equal(3, parsed.Revision);
        Assert.Equal("Synthetic body", parsed.Events[0].Body);
        Assert.Equal("remote", parsed.Events[1].Source);
        Assert.Equal(20, parsed.Events[1].Nonce);
        Assert.Empty(parsed.Events[1].Body);
        Assert.Contains("agent.notifications", GhosttyWire.NotificationsRead());
    }

    [Fact]
    public void WireRefusesOversizeDuplicateSequenceAndIncompleteRecords()
    {
        Assert.Null(GhosttyWire.ParseNotifications(Wire([Message(body: new string('x', 4097))])));
        Assert.Null(GhosttyWire.ParseNotifications(Wire([Message(), Message()])));
        Assert.Null(GhosttyWire.ParseNotifications(Wire([Message(4)])));
        Assert.Null(GhosttyWire.ParseNotifications(Wire([new { seq = 1, id = 7, kind = "closed" }])));
        Assert.Null(GhosttyWire.ParseNotifications("{\"ok\":true,\"result\":null}"));
    }
}
