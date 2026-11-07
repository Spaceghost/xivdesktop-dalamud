using XivDesktop.Core.Layouts;
using XivDesktop.Core.Windows;
using XivDesktop.Core.Workspaces;

namespace XivDesktop.Core.Tests;

public class TerminalPlateTests
{
    [Fact]
    public void MergeKeepsRichWindowDataAndAddsOnlyNativeWorldPanels()
    {
        var browser = new WindowPanel { Id = 1, App = "browser", State = "live", Agent = "host", Pin = "pet", Kind = "pet" };
        var merged = Panels.WorkspaceWindows(new() { Windows = [browser] }, new() { Panels = [
            new() { Id = 1, Kind = "window", Title = "browser" },
            new() { Id = 2, Kind = "terminal", Profile = "shell", Title = "htop", View = "hud", Pin = "hud 0.2 0.7 2.4 0.9", SupportsPlacementCapture = true, Running = true },
            new() { Id = 3, Kind = "terminal", View = "tab", Running = true },
            new() { Id = 4, Kind = "terminal", View = "hidden", Running = true },
            new() { Id = 5, Kind = "terminal", View = "pet", Running = false },
        ] });
        Assert.Equal(new long[] { 1, 2, 4, 5 }, merged.Select(p => p.Id));
        Assert.Same(browser, merged[0]);
        Assert.Equal("profile:shell", merged[1].App);
        Assert.True(merged[1].SupportsPlacementCapture);
        Assert.Equal("hud 0.2 0.7 2.4 0.9", merged[1].Pin);
        Assert.True(merged[2].Hidden);
        Assert.Equal("pin", merged[2].Kind);
        Assert.True(merged[3].IsEnded);
    }

    [Fact]
    public void TerminalIdentityAndMembershipSurvivePersistenceAndReconnect()
    {
        var model = new WorkspaceModel();
        var old = new WindowPanel { Id = 2, Title = "htop", App = "profile:shell", State = "live", Kind = "pet" };
        model.Reconcile([old]);
        Assert.True(model.AssignApp(2, "htop.desktop"));
        model.Move(2, 3);
        var restored = new WorkspaceModel(model.Export());
        var current = old with { Id = 20 };
        restored.Reconcile([current]);
        Assert.Equal("htop.desktop", restored.AppIdOf(20));
        Assert.Equal(3, restored.WorkspaceOf(20));
        var plan = PlatePlanner.Build(new("raid", [new("htop.desktop", 3, "pet", 0)]), [current], p => restored.AppIdOf(p.Id));
        Assert.Equal(20, Assert.Single(plan).PanelId);
    }

    [Fact]
    public void NativeHudAndHiddenTerminalsFollowWorkspaceVisibility()
    {
        var panels = Panels.WorkspaceWindows(WindowListSnapshot.Empty, new() { Panels = [
            new() { Id = 2, Kind = "terminal", View = "hud", Running = true },
            new() { Id = 3, Kind = "terminal", View = "hidden", Running = true },
        ] });
        var model = new WorkspaceModel();
        model.Reconcile(panels);
        model.Move(2, 2);
        Assert.Equal(new[] { new VisibilityChange(2, true), new VisibilityChange(3, false) }, model.Plan(panels));
        model.Switch(2);
        Assert.Empty(model.Plan(panels));
    }

    [Fact]
    public void EndedTerminalsNeverSatisfyPlatePieces()
    {
        var panels = Panels.WorkspaceWindows(WindowListSnapshot.Empty, new() { Panels = [new() { Id = 2, Kind = "terminal", View = "pet", Running = false }] });
        var plan = PlatePlanner.Build(new("raid", [new("htop.desktop", 1, "pet", 0)]), panels, _ => "htop.desktop");
        Assert.Null(Assert.Single(plan).PanelId);
    }
}
