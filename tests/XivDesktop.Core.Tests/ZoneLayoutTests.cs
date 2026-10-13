using XivDesktop.Core.Layouts;

namespace XivDesktop.Core.Tests;

public class ZoneLayoutTests
{
    [Fact]
    public void WaitsForLoadingAndSettleThenAppliesOnlyOnce()
    {
        var tracker = new ZoneLayoutTracker();
        var rule = new ZoneLayoutRule(Workspace: 2);
        Assert.Null(tracker.Update(10, rule, false, false, false, 0));
        Assert.Null(tracker.Update(10, rule, true, false, false, 100));
        Assert.Null(tracker.Update(10, rule, true, false, false, 1099));
        Assert.Equal(rule, tracker.Update(10, rule, true, false, false, 1100));
        Assert.Null(tracker.Update(10, rule, true, false, false, 9000));
    }

    [Fact]
    public void DutyNeedsExplicitOptInAndBusyGuardsPostponeArrival()
    {
        var tracker = new ZoneLayoutTracker();
        var rule = new ZoneLayoutRule("raid");
        tracker.Update(10, rule, true, true, false, 0);
        Assert.Null(tracker.Update(10, rule, true, true, false, 1000));
        var allowed = rule with { AllowInDuty = true };
        tracker.Update(10, allowed, true, true, false, 2000);
        Assert.Null(tracker.Update(10, allowed, true, true, true, 3000));
        Assert.Equal(allowed, tracker.Update(10, allowed, true, true, false, 4000));
    }

    [Fact]
    public void NewArrivalToSameTerritoryAndMappingEditsApplyAgain()
    {
        var tracker = new ZoneLayoutTracker();
        var rule = new ZoneLayoutRule(Workspace: 2);
        tracker.Update(10, rule, true, false, false, 0);
        Assert.Equal(rule, tracker.Update(10, rule, true, false, false, 1000));
        tracker.Invalidate();
        Assert.Null(tracker.Update(10, rule, true, false, false, 2000));
        Assert.Equal(rule, tracker.Update(10, rule, true, false, false, 3000));
        var changed = new ZoneLayoutRule(Workspace: 3);
        Assert.Null(tracker.Update(10, changed, true, false, false, 4000));
        Assert.Equal(changed, tracker.Update(10, changed, true, false, false, 5000));
    }

    [Fact]
    public void LoadingRestartsSettleAndUnmappedOrInvalidRulesLeaveManualDeskAlone()
    {
        var tracker = new ZoneLayoutTracker();
        var rule = new ZoneLayoutRule(Workspace: 2);
        tracker.Update(10, rule, true, false, false, 0);
        tracker.Update(10, rule, false, false, false, 900);
        Assert.Null(tracker.Update(10, rule, true, false, false, 1000));
        Assert.Equal(rule, tracker.Update(10, rule, true, false, false, 2000));
        Assert.Null(tracker.Update(11, null, true, false, false, 3000));
        Assert.Null(tracker.Update(11, new("raid", 2), true, false, false, 4000));
        Assert.Null(tracker.Update(11, new(Workspace: 10), true, false, false, 5000));
        Assert.Null(tracker.Update(0, rule, true, false, false, 6000));
    }
}
