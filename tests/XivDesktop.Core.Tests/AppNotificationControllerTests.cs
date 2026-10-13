using XivDesktop.Core.Notifications;

namespace XivDesktop.Core.Tests;

public class AppNotificationControllerTests
{
    private static AgentNotification Message(long seq, uint id = 1, string source = "local", long nonce = 10,
        string kind = "notification") => new(seq, source, nonce, id, kind, "Fixture", "fixture.desktop", $"Title {seq}", $"Body {seq}", 1);

    private sealed class Toast(NotificationPresentation presentation, Action click) : IAppNotificationToast
    {
        public string Title = presentation.Title;
        public string Body = presentation.Body;
        public bool IsDismissed { get; set; }
        public int Dismissals;
        public void Click() => click();
        public void HideBody() => Body = "";
        public void Dismiss() { Title = Body = ""; IsDismissed = true; Dismissals++; }
    }

    private sealed class Sink : IAppNotificationSink
    {
        public Func<AgentNotification, bool> Suppressed = _ => false;
        public readonly List<Toast> Toasts = [];
        public readonly List<AgentNotification> Focused = [];
        public bool FailShow;
        public bool IsSuppressed(AgentNotification message) => Suppressed(message);
        public IAppNotificationToast Show(AgentNotification message, NotificationPresentation presentation, Action click)
        {
            if (FailShow) throw new InvalidOperationException("fixture unavailable");
            var toast = new Toast(presentation, click);
            Toasts.Add(toast);
            return toast;
        }
        public void Focus(AgentNotification message) => Focused.Add(message);
    }

    [Fact]
    public void ReplacementAndCloseInvalidateOldCallbacks()
    {
        var sink = new Sink();
        using var controller = new AppNotificationController(sink);
        controller.Observe(new(0, []), true, false, 0);
        controller.Observe(new(1, [Message(1)]), true, false, 1);
        var first = Assert.Single(sink.Toasts);
        first.Click();
        Assert.Single(sink.Focused);
        controller.Observe(new(2, [Message(1), Message(2)]), true, false, 2);
        Assert.True(first.IsDismissed);
        Assert.Equal("", first.Body);
        first.Click();
        Assert.Single(sink.Focused);
        var replacement = sink.Toasts[1];
        replacement.Click();
        Assert.Equal(2, sink.Focused.Count);
        controller.Observe(new(3, [Message(3, kind: "closed")]), true, false, 3);
        replacement.Click();
        Assert.Equal(2, sink.Focused.Count);
        Assert.Equal(1, replacement.Dismissals);
    }

    [Fact]
    public void ReplacementBurstUsesFinalStateAndNeverShowsClosedIntermediate()
    {
        var sink = new Sink();
        using var controller = new AppNotificationController(sink);
        controller.Observe(new(0, []), true, false, 0);
        controller.Observe(new(5, [Message(1), Message(2), Message(3), Message(4, 2), Message(5, 2, kind: "closed")]), true, false, 1);
        Assert.Equal("Title 3", Assert.Single(sink.Toasts).Title);
        controller.Observe(new(8, [Message(6, 3), Message(7, 4), Message(8, 5)]), true, false, 2);
        Assert.Equal(4, sink.Toasts.Count); // Coalesced replacements use only one burst slot.
    }

    [Fact]
    public void PrivacyRefreshScrubsExistingToastsWithoutWaitingForPoll()
    {
        var sink = new Sink();
        using var controller = new AppNotificationController(sink);
        controller.Observe(new(0, []), true, false, 0);
        controller.Observe(new(1, [Message(1)]), true, false, 1);
        var toast = Assert.Single(sink.Toasts);
        controller.Refresh(true, true);
        Assert.Equal("", toast.Body);
        sink.Suppressed = _ => true;
        toast.Click(); // Click also checks privacy even before the next framework refresh.
        Assert.Empty(sink.Focused);
        controller.Refresh(true, false);
        Assert.True(toast.IsDismissed);
        Assert.Equal("", toast.Title);
        sink.Suppressed = _ => false;
        controller.Observe(new(1, [Message(1)]), true, false, 2);
        Assert.Single(sink.Toasts); // Revealing does not replay a private notification.
    }

    [Fact]
    public void IdenticalIdsOnDifferentAgentsRemainIndependentAndReconnectInvalidatesOldNonce()
    {
        var sink = new Sink();
        using var controller = new AppNotificationController(sink);
        controller.Observe(new(0, []), true, true, 0);
        controller.Observe(new(2, [Message(1), Message(2, source: "remote")]), true, true, 1);
        Assert.Equal(2, sink.Toasts.Count);
        controller.Observe(new(3, [Message(1), Message(2, source: "remote"), Message(3, nonce: 11)]), true, true, 2);
        Assert.True(sink.Toasts[0].IsDismissed);
        Assert.False(sink.Toasts[1].IsDismissed);
        sink.Toasts[0].Click();
        sink.Toasts[1].Click();
        sink.Toasts[2].Click();
        Assert.Collection(sink.Focused,
            remote => Assert.Equal("remote", remote.Source),
            local => Assert.Equal(11, local.Nonce));
    }

    [Fact]
    public void DisconnectDisableAndRevisionResetBaselineWithoutReplay()
    {
        var sink = new Sink();
        using var controller = new AppNotificationController(sink);
        controller.Observe(new(0, []), true, true, 0);
        controller.Observe(new(1, [Message(1)]), true, true, 1);
        controller.Observe(null, true, true, 2);
        Assert.True(sink.Toasts[0].IsDismissed);
        controller.Observe(new(3, [Message(2), Message(3)]), true, true, 3);
        Assert.Single(sink.Toasts);
        controller.Observe(new(4, [Message(4)]), true, true, 4);
        controller.Refresh(false, true);
        Assert.True(sink.Toasts[1].IsDismissed);
        controller.Observe(new(5, [Message(5)]), false, true, 5);
        controller.Observe(new(6, [Message(6)]), true, true, 6);
        Assert.Equal(2, sink.Toasts.Count);
        controller.Observe(new(7, [Message(7)]), true, true, 7);
        controller.Observe(new(1, [Message(1)]), true, true, 8);
        Assert.True(sink.Toasts[2].IsDismissed);
        Assert.Equal(3, sink.Toasts.Count);
    }

    [Fact]
    public void CloseStillDismissesWhenBurstLimitIsExhaustedAndExpiredToastCannotFocus()
    {
        var sink = new Sink();
        using var controller = new AppNotificationController(sink);
        controller.Observe(new(0, []), true, false, 0);
        controller.Observe(new(4, Enumerable.Range(1, 4).Select(i => Message(i, (uint)i)).ToArray()), true, false, 1);
        controller.Observe(new(6, [Message(5, kind: "closed"), Message(6, 5)]), true, false, 2);
        Assert.Equal(4, sink.Toasts.Count);
        Assert.True(sink.Toasts[0].IsDismissed);
        sink.Toasts[1].IsDismissed = true;
        sink.Toasts[1].Click();
        Assert.Empty(sink.Focused);
        controller.Refresh(true, false);
        Assert.Equal(1, sink.Toasts[1].Dismissals);
    }

    [Fact]
    public void DisableAndEnableBetweenPollsStillEstablishesFreshBaseline()
    {
        var sink = new Sink();
        using var controller = new AppNotificationController(sink);
        controller.Observe(new(0, []), true, true, 0);
        controller.Observe(new(1, [Message(1)]), true, true, 1);
        controller.Refresh(false, true);
        controller.Refresh(true, true);
        controller.Observe(new(2, [Message(2)]), true, true, 2);
        Assert.Single(sink.Toasts);
        controller.Observe(new(3, [Message(3)]), true, true, 3);
        Assert.Equal(2, sink.Toasts.Count);
    }

    [Fact]
    public void ResetAfterSinkFailureAndDisposePreventStaleCallbacks()
    {
        var sink = new Sink();
        var controller = new AppNotificationController(sink);
        controller.Observe(new(0, []), true, false, 0);
        controller.Observe(new(1, [Message(1)]), true, false, 1);
        sink.FailShow = true;
        Assert.Throws<InvalidOperationException>(() => controller.Observe(new(2, [Message(2, 2)]), true, false, 2));
        controller.Reset();
        Assert.True(sink.Toasts[0].IsDismissed);
        sink.FailShow = false;
        controller.Observe(new(3, [Message(3)]), true, false, 3);
        Assert.Single(sink.Toasts);
        controller.Observe(new(4, [Message(4)]), true, false, 4);
        controller.Dispose();
        sink.Toasts[1].Click();
        Assert.Empty(sink.Focused);
        controller.Observe(new(5, [Message(5)]), true, false, 5);
        Assert.Equal(2, sink.Toasts.Count);
    }
}
