using XivDesktop.Core.Palette;
using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Tests;

public class OpenWithTests
{
    [Theory]
    [InlineData("editor %f", "editor '/home/a file.txt'")]
    [InlineData("editor --file=%f", "editor '--file=/home/a file.txt'")]
    [InlineData("editor %u", "editor file:///home/a%20file.txt")]
    [InlineData("editor %U", "editor file:///home/a%20file.txt")]
    [InlineData("editor %% %f %i %c %k", "editor % '/home/a file.txt'")]
    [InlineData("editor", "editor '/home/a file.txt'")]
    public void ExpandsSelectedFile(string exec, string expected)
        => Assert.Equal(expected, ExecLine.OpenCommand(exec, ["/home/a file.txt"]));

    [Fact]
    public void MultipleFilesAndFlatpakForwardingKeepEachArgumentSeparate()
    {
        Assert.Equal("editor '/a b' '/c d'", ExecLine.OpenCommand("editor %F", ["/a b", "/c d"]));
        Assert.Equal("flatpak run --file-forwarding editor @@u file:///a%20b file:///c%20d @@",
            ExecLine.OpenCommand("flatpak run --file-forwarding editor @@u %U @@", ["/a b", "/c d"]));
    }

    [Fact]
    public void FilenamesCannotRunShellCommandsAndUriDelimitersAreEscaped()
    {
        var path = "/home/$(touch /tmp/unwanted);'x#?.txt";
        Assert.Equal("editor " + ExecLine.ShellQuote(path), ExecLine.OpenCommand("editor %f", [path]));
        Assert.Equal("file:///home/%24%28touch%20/tmp/unwanted%29%3B%27x%23%3F.txt", ExecLine.FileUri(path));
    }

    [Theory]
    [InlineData("editor %F-tail")]
    [InlineData("editor %f %u")]
    [InlineData("%f editor")]
    [InlineData("editor \"unterminated")]
    [InlineData("editor\n%f")]
    public void RejectsMalformedFieldLayouts(string exec)
        => Assert.Null(ExecLine.OpenCommand(exec, ["/tmp/file"]));

    [Theory]
    [InlineData("relative.txt")]
    [InlineData("/file\n/desktop other")]
    [InlineData("/file\0")]
    public void RejectsNonAbsoluteOrControlCharacterPaths(string file)
        => Assert.Null(ExecLine.OpenCommand("editor %f", [file]));

    [Fact]
    public void SingleFileAppsGetOneLaunchPerFileAndWinePathsAreMapped()
    {
        var app = new AppInfo { Id = "editor.desktop", Name = "Editor", Exec = "editor %f", AgentId = "editor", Terminal = true };
        var plan = OpenWithPlan.Build(app, [@"Z:\home\a.txt", @"z:\home\b.txt"]);
        Assert.Null(plan.Error);
        Assert.Equal(2, plan.Apps.Count);
        Assert.Equal("editor /home/a.txt", plan.Apps[0].Command);
        Assert.Equal("editor /home/b.txt", plan.Apps[1].Command);
        Assert.All(plan.Apps, a => { Assert.Null(a.AgentId); Assert.True(a.Terminal); Assert.Equal(app.Id, a.Id); });
        Assert.Equal(LaunchKind.Terminal, LaunchPlan.Plan(plan.Apps[0]).Target!.Kind);
        Assert.Single(OpenWithPlan.Build(app with { Exec = "editor %F" }, ["/a", "/b"]).Apps);
    }

    [Fact]
    public void InaccessibleExecAndNonHostDrivesReturnActionableErrors()
    {
        var app = new AppInfo { Id = "x", Name = "X" };
        Assert.Contains("not visible", OpenWithPlan.Build(app, ["/file"]).Error!);
        Assert.Contains("drive Z:", OpenWithPlan.Build(app with { Exec = "x %f" }, [@"C:\file"]).Error!);
        Assert.Empty(OpenWithPlan.Build(app with { Exec = "x %F" }, []).Apps);
    }

    [Fact]
    public void MimeIndexAndAgentMergeRetainDeclaredFileHandlers()
    {
        using var tree = new TempTree();
        tree.Write("/usr/share/applications/editor.desktop", "[Desktop Entry]\nType=Application\nName=Editor\nExec=editor %U\nMimeType=text/plain;text/plain;image/svg+xml;\n");
        var catalog = AppCatalog.Scan(tree.Paths);
        Assert.Equal("editor.desktop", Assert.Single(catalog.AppsForMime("text/plain")).Id);
        Assert.Empty(catalog.AppsForMime("application/pdf"));
        var agent = AgentCatalog.Build([new AgentApp("editor", "Editor", "", [])], _ => null, catalog);
        Assert.Equal("editor %U", agent.Find("editor")!.Exec);
        Assert.Single(agent.AppsForMime("text/plain"));
    }

    [Fact]
    public void PaletteOffersFilePickerForAppRows()
    {
        var row = new PaletteItem { Provider = PaletteProvider.App, Title = "Editor", AppId = "editor.desktop", Command = new PaletteCommand(PaletteCommand.Launch, "editor.desktop") };
        var open = Assert.Single(PaletteEngine.RowActions(row), a => a.Command.Kind == PaletteCommand.OpenFile);
        Assert.Equal("Ctrl+O", open.Hint);
        Assert.Equal("editor.desktop", open.Command.Arg);
    }
}
