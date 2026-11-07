using XivDesktop.Core.Layouts;
using XivDesktop.Core.Palette;
using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Tests;

public class DeskPlateTests
{
    private static DeskPlate Plate(params PlatePiece[] pieces) => new("raid", pieces);
    private static PlatePiece Piece(string app, int order = 0) => new(app, 2, "hud 0.1 0.5", order);

    [Fact]
    public void PlacementArgsSurviveBothWireParsersAndLegacyRepliesRemainUsable()
    {
        const string pin = "at 0.4 3.2 1.7 2.4 -0.2";
        var windows = GhosttyWire.ParseWindowList("""{"ok":true,"result":{"windows":[{"id":4,"pin":"at 0.4 3.2 1.7 2.4 -0.2","state":"live"}]}}""")!;
        Assert.Equal(pin, Assert.Single(windows.Windows).Pin);
        Assert.True(windows.Windows[0].SupportsPlacementCapture);
        Assert.Equal(pin, Assert.Single(Panels.FromWindows(windows).Panels).Pin);
        var native = Panels.ParseList("""{"ok":true,"result":{"panels":[{"id":4,"pin":"hud 0.2 0.7 2.4 0.9"}]}}""")!;
        Assert.Equal("hud 0.2 0.7 2.4 0.9", Assert.Single(native.Panels).Pin);
        var legacy = GhosttyWire.ParseWindowList("""{"ok":true,"result":{"windows":[{"id":4}]}}""")!;
        Assert.Equal("", Assert.Single(legacy.Windows).Pin);
        Assert.False(legacy.Windows[0].SupportsPlacementCapture);
        var unavailable = GhosttyWire.ParseWindowList("""{"ok":true,"result":{"windows":[{"id":4,"pin":null}]}}""")!;
        Assert.True(unavailable.Windows[0].SupportsPlacementCapture);
        Assert.Equal("", unavailable.Windows[0].Pin);
    }

    [Fact]
    public void RepeatedAppsConsumeDistinctLivePanelsAndLaunchTheRest()
    {
        var plan = PlatePlanner.Build(Plate(Piece("browser"), Piece("browser", 1), Piece("browser", 2)),
            [new() { Id = 9, App = "browser", State = "live" }, new() { Id = 2, App = "BROWSER", State = "live" }, new() { Id = 3, App = "browser", State = "pending" }, new() { Id = 4, App = "browser", State = "ended" }], w => w.App);
        Assert.Equal(new long?[] { 2, 9, null }, plan.Select(p => p.PanelId));
    }

    [Fact]
    public void UsesResolvedAppIdentityRatherThanTitleAndPreservesPlacementAndWorkspace()
    {
        var wanted = Piece("editor.desktop");
        var plan = PlatePlanner.Build(Plate(wanted), [new() { Id = 5, App = "different-command", Title = "browser", State = "live" }], _ => "editor.desktop");
        Assert.Equal(5, Assert.Single(plan).PanelId);
        Assert.Equal(wanted, plan[0].Piece);
        Assert.Null(PlatePlanner.Validate(Plate(wanted)));
    }

    [Fact]
    public void InvalidSavedPlatesCannotReachTheBackend()
    {
        Assert.NotNull(PlatePlanner.Validate(Plate()));
        Assert.NotNull(PlatePlanner.Validate(Plate(Piece("app") with { Workspace = 0 })));
        Assert.NotNull(PlatePlanner.Validate(Plate(Piece("app") with { Pin = "pet\nclose 1" })));
        Assert.NotNull(PlatePlanner.Validate(Plate(Piece("app")) with { Name = "\n" }));
        Assert.NotNull(PlatePlanner.Validate(Plate(Piece("app")) with { Workspace = 10 }));
        Assert.NotNull(PlatePlanner.Validate(Plate(Piece("app") with { Order = -1 })));
    }

    [Fact]
    public void LaunchWaitRequiresItsOwnRequestAndKeepsPanelIdentityAfterHistoryExpires()
    {
        var pending = new PlateRequest(10, 0, true);
        pending.Update(new() { Requests = [new() { Request = 11, Ok = true, PanelId = 9 }] }, 1);
        Assert.False(pending.Complete);
        pending.Update(new() { Requests = [new() { Request = 10, Ok = true, PanelId = 8 }], Windows = [new() { Id = 8, State = "pending" }] }, 2);
        Assert.False(pending.Complete);
        pending.Update(new() { Windows = [new() { Id = 8, State = "live" }] }, 3);
        Assert.True(pending.Complete);
        Assert.Equal(8, pending.PanelId);
    }

    [Fact]
    public void QueuedPlacementNeedsAcknowledgementAndReportsFailureOrTimeout()
    {
        var placement = new PlateRequest(10, 0, false);
        placement.Update(WindowListSnapshot.Empty, 100);
        Assert.False(placement.Complete);
        placement.Update(new() { Requests = [new() { Request = 10, Ok = false, Error = "bad pin" }] }, 101);
        Assert.Equal("bad pin", placement.Error);
        var order = new PlateRequest(11, 0, false);
        order.Update(new() { Requests = [new() { Request = 11, Ok = true }] }, 2);
        Assert.True(order.Complete);
        var timeout = new PlateRequest(12, 0, false);
        timeout.Update(WindowListSnapshot.Empty, PlateRequest.TimeoutMs);
        Assert.NotNull(timeout.Error);
        Assert.False(timeout.Complete);
    }

    [Fact]
    public void PreviewShowsAllPiecesAndPaletteSelectsTheNamedPlate()
    {
        var plate = Plate(Piece("spreadsheet"), Piece("voice", 1) with { Workspace = 3, Pin = "pet" });
        var row = Assert.Single(PaletteEngine.Build(new() { Plates = [plate] }, ">plate raid"));
        Assert.Equal(PaletteCommand.Plate, row.Command.Kind);
        Assert.Equal("raid", row.Command.Arg);
        Assert.Contains("spreadsheet: workspace 2, hud 0.1 0.5", row.Subtitle);
        Assert.Contains("voice: workspace 3, pet", row.Subtitle);
        Assert.Empty(PaletteEngine.Build(new() { Plates = [plate] }, ">plate cozy"));
    }
}
