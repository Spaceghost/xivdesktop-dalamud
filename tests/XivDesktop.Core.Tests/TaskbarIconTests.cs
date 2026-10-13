using XivDesktop.Shared;

namespace XivDesktop.Core.Tests;

public class TaskbarIconTests
{
    [Fact]
    public void SharedIconsReadAndValidateOnceAcrossManyButtonsAndRefreshWithoutNewBuffers()
    {
        var reads = 0;
        var decodes = 0;
        var cache = new TaskbarIconCache(_ => { reads++; return [1, 2, 3]; }, _ => { decodes++; return true; });
        var first = cache.Get("browser.png", 0);
        for (var i = 1; i < 20; i++) Assert.Same(first, cache.Get("browser.png", i));
        Assert.Equal(1, reads);
        Assert.Equal(1, decodes);
        Assert.Same(first, cache.Get("browser.png", 30_000));
        Assert.Equal(2, reads);
        Assert.Equal(1, decodes);
    }

    [Fact]
    public void CorruptAndMissingIconsFallBackAndCanRecoverAfterRefresh()
    {
        var reads = 0;
        var usable = false;
        var cache = new TaskbarIconCache(_ => { reads++; return usable ? [1] : [0]; }, b => b[0] == 1);
        Assert.Null(cache.Get("icon.png", 0));
        Assert.Null(cache.Get("icon.png", 10));
        Assert.Equal(1, reads);
        usable = true;
        Assert.NotNull(cache.Get("icon.png", 30_000));
        var missing = new TaskbarIconCache(_ => throw new IOException(), _ => true);
        Assert.Null(missing.Get("absent.png", 0));
        Assert.Null(missing.Get(null, 0));
    }

    [Fact]
    public void BoundedCacheEvictsLeastRecentlyUsedIcons()
    {
        var reads = new List<string>();
        var cache = new TaskbarIconCache(p => { reads.Add(p); return [1]; }, _ => true, 2);
        cache.Get("a", 0); cache.Get("b", 1); cache.Get("a", 2); cache.Get("c", 3);
        cache.Get("a", 4); cache.Get("b", 5);
        Assert.Equal(new[] { "a", "b", "c", "b" }, reads);
    }

    [Fact]
    public void NewLivePanelsPulseButInitialSnapshotAndVeilDoNotExposeArrivals()
    {
        var arrivals = new TaskbarArrivals();
        arrivals.Update([(1, "live")], false, 0);
        Assert.False(arrivals.Highlight(1, 0));
        arrivals.Update([(1, "live"), (2, "pending")], false, 10);
        Assert.False(arrivals.Highlight(2, 10));
        arrivals.Update([(1, "live"), (2, "live")], false, 20);
        Assert.True(arrivals.Highlight(2, 21));
        Assert.False(arrivals.Highlight(2, 4020));
        arrivals.Update([(1, "live"), (2, "live"), (3, "live")], true, 4030);
        Assert.False(arrivals.Highlight(3, 4030));
        arrivals.Update([(1, "live"), (2, "live"), (3, "live")], false, 4040);
        Assert.False(arrivals.Highlight(3, 4040));
    }

    [Fact]
    public void IconSizeRequestsAreOptionalAndBounded()
    {
        Assert.DoesNotContain("icon_size", XivDesktop.Core.Windows.GhosttyWire.AgentWindowsRefresh());
        Assert.Contains("\"icon_size\":48", XivDesktop.Core.Windows.GhosttyWire.AgentWindowsRefresh(48));
        Assert.Contains("\"icon_size\":16", XivDesktop.Core.Windows.GhosttyWire.AgentWindowsRefresh(0));
        Assert.Contains("\"icon_size\":512", XivDesktop.Core.Windows.GhosttyWire.AgentWindowsRefresh(9999));
    }

    [Fact]
    public void IconPathsAreAdditiveAndMissingLegacyFieldsRemainNull()
    {
        var old = IpcContract.ParseWindows("""{"windows":[{"id":1,"title":"Browser"}]}""")!;
        Assert.Null(Assert.Single(old.Windows).IconPath);
        var json = IpcContract.Serialize(new AppSummary { Id = "browser", IconPath = "Z:\\icons\\browser.png" });
        Assert.Equal("Z:\\icons\\browser.png", Assert.Single(IpcContract.ParseApps("[" + json + "]")).IconPath);
    }
}
