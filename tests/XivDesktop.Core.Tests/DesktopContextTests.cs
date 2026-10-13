using XivDesktop.Core.Windows;
using XivDesktop.Core.Workspaces;

namespace XivDesktop.Core.Tests;

public class DesktopContextTests
{
    private static WindowPanel W(long id, bool hidden = false, string kind = "pet")
        => new() { Id = id, Title = "Panel " + id, Kind = kind, State = "live", Hidden = hidden };

    [Fact]
    public void JobMappingDoesNotOverrideManualWorkspaceSwitchesBetweenJobChanges()
    {
        var tracker = new JobWorkspaceTracker();
        var map = new Dictionary<uint, int> { [19] = 2, [24] = 4 };
        Assert.Equal(2, tracker.Update(19, map));
        Assert.Null(tracker.Update(19, map));
        Assert.Equal(4, tracker.Update(24, map));
        Assert.Null(tracker.Update(24, map));
        Assert.Null(tracker.Update(1, map));
        Assert.Equal(2, tracker.Update(19, map));
    }

    [Fact]
    public void LoginAndMappingChangesAreHandledAndInvalidWorkspacesIgnored()
    {
        var tracker = new JobWorkspaceTracker();
        var map = new Dictionary<uint, int> { [19] = 3, [0] = 7, [24] = 42 };
        Assert.Null(tracker.Update(0, map));
        Assert.Equal(3, tracker.Update(19, map));
        map[19] = 9;
        Assert.Equal(9, tracker.Update(19, map));
        map.Remove(19);
        Assert.Null(tracker.Update(19, map));
        Assert.Null(tracker.Update(24, map));
        Assert.Null(tracker.Update(0, map));
        Assert.Equal(9, tracker.Update(19, new Dictionary<uint, int> { [19] = 9 }));
    }

    [Fact]
    public void StowRestoresOnlyWhatWasVisibleAndNeverTouchesTabsOrExceptions()
    {
        var stow = new PanelStow();
        var panels = new[] { W(1), W(2, hidden: true), W(3), W(4, kind: "tab") };
        Assert.Equal(new[] { new VisibilityChange(1, true) }, stow.Plan(panels, true, w => w.Hidden == true, w => w.Id == 3));
        Assert.True(stow.Owns(1));
        Assert.True(stow.Owns(2));
        Assert.False(stow.Owns(3));
        Assert.False(stow.Owns(4));
        var hidden = panels.Select(w => w.Id == 1 ? w with { Hidden = true } : w).ToList();
        Assert.Empty(stow.Plan(hidden, true, w => w.Hidden == true, w => w.Id == 3));
        Assert.Equal(new[] { new VisibilityChange(1, false) }, stow.Plan(hidden, false, w => w.Hidden == true, _ => false));
        Assert.True(stow.Owns(1)); // retry restoration until it succeeds
        stow.Restored(1);
        Assert.False(stow.Owns(1));
        Assert.False(stow.Owns(2));
        Assert.True(stow.PreservesHidden(2));
        stow.Reveal(2);
        Assert.False(stow.PreservesHidden(2));
    }

    [Fact]
    public void NewlyOpenedPanelsAreStowedAndChangingExceptionsRestoresThem()
    {
        var stow = new PanelStow();
        stow.Plan([W(1)], true, w => w.Hidden == true, _ => false);
        Assert.Equal(new[] { new VisibilityChange(2, true) }, stow.Plan([W(1, true), W(2)], true, w => w.Hidden == true, _ => false));
        Assert.Equal(new[] { new VisibilityChange(1, false) }, stow.Plan([W(1, true), W(2, true)], true, w => w.Hidden == true, w => w.Id == 1));
        stow.Restored(1);
        Assert.False(stow.Owns(1));
        Assert.True(stow.Owns(2));
        stow.Plan([], false, _ => false, _ => false);
        Assert.False(stow.Owns(2));
    }

    [Fact]
    public void StowIncludesWorldTerminalsAdoptedHudAndFullscreenPanelsOnce()
    {
        var full = new PanelListSnapshot { Panels =
        [
            new PanelInfo { Id = 1, Kind = "window", View = "pet", Title = "Window" },
            new PanelInfo { Id = 2, Kind = "terminal", View = "pin" },
            new PanelInfo { Id = 3, Kind = "adopted", View = "hud" },
            new PanelInfo { Id = 4, Kind = "terminal", View = "hidden" },
            new PanelInfo { Id = 5, Kind = "window", View = "full" },
            new PanelInfo { Id = 6, Kind = "terminal", View = "tab" },
        ] };
        var world = PanelStow.WorldPanels([W(1), W(5, kind: "full")], full);
        Assert.Equal(5, world.Count);
        var stow = new PanelStow();
        var changes = stow.Plan(world, true, w => w.Hidden == true, _ => false);
        Assert.Equal(new long[] { 1, 2, 3, 5 }, changes.Select(c => c.Id).Order());
        Assert.All(changes, c => Assert.True(c.Hide));
        Assert.DoesNotContain(changes, c => c.Id == 4 || c.Id == 6);
    }

    [Fact]
    public void NegativePersistedWorkspaceDoesNotAccidentallyBecomeAScratchpad()
    {
        var model = new WorkspaceModel([new WorkspaceEntry { PanelId = 1, Workspace = -5 }]);
        Assert.Equal(1, model.WorkspaceOf(1));
        Assert.False(model.IsScratchpad(1));
    }

    [Fact]
    public void OverlappingConditionsDoNotFlashTheDesk()
    {
        var latch = new StowLatch();
        Assert.True(latch.Update(true, 100));
        Assert.True(latch.Update(false, 200));
        Assert.True(latch.Update(false, 699));
        Assert.True(latch.Update(true, 700)); // duty starts before the settle time ends
        Assert.True(latch.Update(false, 1000));
        Assert.False(latch.Update(false, 1500));
    }

    [Fact]
    public void ScratchpadStaysHiddenAcrossWorkspacesAndSummonsOnePanelAtATime()
    {
        var model = new WorkspaceModel();
        var panels = new[] { W(1), W(2), W(3) };
        model.Reconcile(panels);
        model.Move(1, 0);
        model.Move(2, 0);
        Assert.True(model.IsScratchpad(1));
        Assert.False(model.IsScratchpad(99));
        Assert.Equal(new[] { new VisibilityChange(1, true), new VisibilityChange(2, true) }, model.Plan(panels));
        model.Switch(5);
        Assert.Equal(1L, model.SummonScratchpad(panels));
        Assert.Equal(new[] { new VisibilityChange(2, true), new VisibilityChange(3, true) }, model.Plan(panels));
        Assert.Equal(2L, model.SummonScratchpad(panels));
        Assert.Equal(new[] { new VisibilityChange(1, true), new VisibilityChange(3, true) }, model.Plan(panels));
        model.DismissScratchpad();
        Assert.Equal(3, model.Plan(panels).Count);
    }

    [Fact]
    public void OneScratchpadPanelTogglesAndScratchpadHidingWorksWithoutWorkspaceHiding()
    {
        var model = new WorkspaceModel();
        var panels = new[] { W(1), W(2) };
        model.Reconcile(panels);
        model.Move(1, 0);
        model.Switch(5);
        Assert.Equal(new[] { new VisibilityChange(1, true) }, model.Plan(panels, hideWorkspaces: false));
        Assert.Equal(1L, model.SummonScratchpad(panels));
        Assert.Empty(model.Plan(panels, hideWorkspaces: false));
        Assert.Null(model.SummonScratchpad(panels));
        var saved = new WorkspaceModel(model.Export());
        Assert.True(saved.IsScratchpad(1));
        Assert.Null(saved.ShownScratchpad);
        Assert.Equal(new[] { new VisibilityChange(1, true) }, saved.Plan(panels));
    }

    [Fact]
    public void ScratchpadAssignmentSurvivesReconnectAndSummonedPanelCanEnd()
    {
        var model = new WorkspaceModel();
        model.Reconcile([W(1)]);
        model.Move(1, 0);
        model.Reconcile([W(2) with { Title = "Panel 1" }]);
        Assert.True(model.IsScratchpad(2));
        Assert.Equal(2L, model.SummonScratchpad([W(2)]));
        model.Reconcile([W(2) with { State = "ended" }]);
        Assert.Null(model.ShownScratchpad);
    }
}
