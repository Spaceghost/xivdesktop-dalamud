namespace XivDesktop.Core.Tests;

public class SvgIconRequestTests
{
    private static AppInfo App(string id, string icon) => new() { Id = id, Name = id, Icon = icon, IconKind = IconKind.SvgOnly, Command = "editor --new", Exec = "editor %F" };

    [Fact]
    public void CatalogWaitsForFilesAndRecalculatesStatsWithoutChangingScanEvidence()
    {
        var app = App("editor", "custom.svg");
        var source = new AppCatalog([app], new CatalogStats { FilesRead = 12, IconsSvgOnly = 1 }, ["scan warning"], DateTimeOffset.UtcNow);
        var replies = new SvgIconReply[] { new("custom.svg", 48, "/cache/rendered.png") };
        var waiting = SvgIconRequests.ApplyCatalog(source, 48, replies, _ => null);
        Assert.Same(app, Assert.Single(waiting.Apps));
        var ready = SvgIconRequests.ApplyCatalog(source, 48, replies, p => p);
        Assert.Equal(1, ready.Stats.IconsPng);
        Assert.Equal(0, ready.Stats.IconsSvgOnly);
        Assert.Equal(12, ready.Stats.FilesRead);
        Assert.Equal(source.ScannedAt, ready.ScannedAt);
        Assert.Equal(source.Errors, ready.Errors);
        Assert.Equal(app.Exec, ready.Find("editor")!.Exec);
        Assert.Equal(IconKind.SvgOnly, source.Apps[0].IconKind);
    }

    [Fact]
    public void RequestsPreserveNamesAndRejectBatchesTheHostCannotAccept()
    {
        var json = XivDesktop.Core.Windows.GhosttyWire.AgentIconsRender(new(48, ["/icons/custom icon.svg", "theme-icon"]));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("agent.icons.render", doc.RootElement.GetProperty("method").GetString());
        var parameters = doc.RootElement.GetProperty("params");
        Assert.Equal(48, parameters.GetProperty("size").GetInt32());
        Assert.Equal("/icons/custom icon.svg", parameters.GetProperty("icons")[0].GetString());
        Assert.Throws<ArgumentException>(() => XivDesktop.Core.Windows.GhosttyWire.AgentIconsRender(new(48, [])));
        Assert.False(SvgIconRequests.ValidName(new string('é', 513)));
    }

    [Fact]
    public void AgentIconMetadataRepliesParseWithoutBecomingAppRows()
    {
        var reply = XivDesktop.Core.Windows.GhosttyWire.ParseAgentIcons("""{"ok":true,"result":[{"icon":"custom.svg","size":48,"png":"/cache/custom.png"},{"icon":"bad","size":999,"png":"/cache/bad.png"}]}""");
        Assert.Equal(new SvgIconReply("custom.svg",48,"/cache/custom.png"), Assert.Single(reply));
        Assert.Empty(XivDesktop.Core.Windows.GhosttyWire.ParseAgentIcons("invalid"));
        Assert.Empty(XivDesktop.Core.Windows.GhosttyWire.ParseAgentIcons("""{"ok":false,"error":"unknown method"}"""));
    }

    [Fact]
    public void SharedIconsAreRequestedOnceAndLargeCataloguesLoseNoNames()
    {
        var apps = Enumerable.Range(0, 130).Select(i => App("app" + i, "icon" + i)).Append(App("shared", "icon0"));
        var batches = SvgIconRequests.Build(apps, 48);
        Assert.Equal(new[] { 64, 64, 2 }, batches.Select(b => b.Icons.Count));
        Assert.Equal(130, batches.SelectMany(b => b.Icons).Distinct().Count());
        Assert.All(batches, b => Assert.Equal(48, b.Size));
    }

    [Fact]
    public void InvalidNamesAndAlreadyResolvedIconsAreExcluded()
    {
        var batches = SvgIconRequests.Build([App("a", "icon\nother"), App("b", "good") with { IconKind = IconKind.Png }, App("c", "/home/user/icon.svg")], 64);
        Assert.Equal(new[] { "/home/user/icon.svg" }, Assert.Single(batches).Icons);
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgIconRequests.Build([], 0));
    }

    [Fact]
    public void RepliesOnlyChangeMatchingIconMetadataAndPreserveLaunchSemantics()
    {
        var a = App("editor", "shared");
        var b = App("terminal", "shared") with { Terminal = true, AgentId = "host-terminal" };
        var request = Assert.Single(SvgIconRequests.Build([a,b],48));
        var result = SvgIconRequests.Apply([a,b],request,[new("shared",48,"/cache/icon.png")],p => "Z:" + p);
        Assert.All(result, app => Assert.Equal("Z:/cache/icon.png",app.IconPath));
        Assert.Equal(a.Command,result[0].Command);
        Assert.Equal(a.Exec,result[0].Exec);
        Assert.Null(result[0].AgentId);
        Assert.True(result[1].Terminal);
        Assert.Equal(b.AgentId,result[1].AgentId);
        Assert.Equal(IconKind.SvgOnly,a.IconKind);
    }

    [Fact]
    public void StaleSizeUnsolicitedIconsAndUnusablePathsDoNotReplaceFallbacks()
    {
        var app = App("a","wanted");
        var request = Assert.Single(SvgIconRequests.Build([app],48));
        var replies = new SvgIconReply[] { new("wanted",64,"/cache/old.png"), new("other",48,"/cache/other.png"), new("wanted",48,"icon.png"), new("wanted",48,"/cache/icon.svg") };
        Assert.Same(app, Assert.Single(SvgIconRequests.Apply([app],request,replies,p => p)));
        Assert.Same(app, Assert.Single(SvgIconRequests.Apply([app],request,[new("wanted",48,"/cache/icon.png")],_ => null)));
    }
}
