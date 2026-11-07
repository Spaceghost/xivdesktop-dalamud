using XivDesktop.Core.Palette;
using XivDesktop.Core.Windows;
using XivDesktop.Shared;

namespace XivDesktop.Core.Tests;

public class DesktopActionTests
{
    private const string Entry = """
        [Desktop Action private]
        Name=Private Window
        Exec=browser --private %U
        [Desktop Entry]
        Type=Application
        Name=Browser
        Exec=browser %U
        Actions=new;private;new;missing;broken;unnamed;
        [Desktop Action new]
        Name[de]=Neues Fenster
        Name[en]=New Window
        Exec=browser --new-window "$(touch /tmp/unwanted)" %U
        Icon=browser-new
        [Desktop Action hidden]
        Name=Unlisted
        Exec=unlisted
        [Desktop Action broken]
        Name=Broken
        Exec=browser "unterminated
        [Desktop Action unnamed]
        Exec=browser
        """;

    [Fact]
    public void OnlyListedValidActionsAppearInDeclaredOrderWithoutDuplicates()
    {
        var entry = DesktopEntryParser.Parse(Entry, "browser.desktop")!;
        Assert.Equal("browser %U", entry.Exec);
        Assert.Equal(new[] { "new", "private" }, entry.Actions.Select(a => a.Id));
        Assert.Equal("New Window", entry.Actions[0].Name);
        Assert.Equal("browser-new", entry.Actions[0].Icon);
        Assert.Equal("browser --new-window '$(touch /tmp/unwanted)'", entry.Actions[0].Command);
        Assert.Equal("browser --private", entry.Actions[1].Command);
    }

    [Fact]
    public void UnlistedSampleActionDoesNotLeakIntoMainCommand()
    {
        var entry = Samples.Parse("basic.desktop");
        Assert.Empty(entry.Actions);
        Assert.Equal("gnome-text-editor %U", entry.Exec);
    }

    [Fact]
    public void DuplicateActionGroupsKeepFirstDefinition()
    {
        var entry = DesktopEntryParser.Parse(Entry + "\n[Desktop Action private]\nName=Other\nExec=other\n", "browser.desktop")!;
        Assert.Equal("browser --private", entry.Actions[1].Command);
    }

    [Fact]
    public void RejectsCommandsContainingLineBreaks()
    {
        // A decoded line break cannot be delivered as a second game/terminal command.
        var broken = DesktopEntryParser.Parse("[Desktop Entry]\nActions=x;\n[Desktop Action x]\nName=X\nExec=browser \\nfoo\n", "x.desktop")!;
        Assert.Empty(broken.Actions);
    }

    [Fact]
    public void ScanAndAgentMergePreserveActionsAndTerminalHandling()
    {
        using var tree = new TempTree();
        tree.Write("/usr/share/applications/browser.desktop", Entry + "\n[Desktop Entry]\nTerminal=true\n");
        var scanned = AppCatalog.Scan(tree.Paths);
        var app = scanned.Find("browser.desktop")!;
        Assert.Equal(2, app.Actions.Count);
        var merged = AgentCatalog.Build([new AgentApp("browser", "Browser", "", [])], _ => null, scanned);
        var agent = merged.Find("browser.desktop")!;
        Assert.Equal(app.Actions, agent.Actions);
        Assert.True(agent.Terminal);
        Assert.Equal(LaunchKind.AgentApp, LaunchPlan.Plan(agent).Target!.Kind);
        var action = LaunchPlan.ActionApp(agent, "private")!;
        Assert.Null(action.AgentId);
        Assert.Equal(LaunchKind.Terminal, LaunchPlan.Plan(action).Target!.Kind);
        Assert.Equal("browser --private", LaunchPlan.Plan(action).Target!.Value);
        Assert.Null(LaunchPlan.ActionApp(agent, "unknown"));
    }

    [Theory]
    [InlineData("org.example.Editor.desktop")]
    [InlineData("editor with spaces.desktop")]
    public void MacroUsesStableDesktopId(string id)
        => Assert.Equal("/desktop launch " + id, LaunchPlan.MacroLine(id));

    [Theory]
    [InlineData("")]
    [InlineData("bad\n/other")]
    [InlineData("bad\r/other")]
    public void RejectsInvalidMacroIds(string id)
        => Assert.Throws<ArgumentException>(() => LaunchPlan.MacroLine(id));

    [Fact]
    public void AppRowsOfferIndependentActionAndMacroCommands()
    {
        var app = DesktopEntryParser.Parse(Entry, "browser.desktop")!;
        var item = new PaletteItem
        {
            Provider = PaletteProvider.App, Title = app.Name, AppId = app.Id,
            AppActions = app.Actions, Command = new PaletteCommand(PaletteCommand.Launch, app.Id),
        };
        var actions = PaletteEngine.RowActions(item);
        Assert.Equal(5, actions.Count);
        Assert.Equal(item.Command, actions[0].Command);
        Assert.Equal("/desktop launch browser.desktop", actions[1].Command.Arg);
        Assert.Equal(PaletteCommand.Copy, actions[1].Command.Kind);
        Assert.Equal(PaletteCommand.LaunchAction, actions[3].Command.Kind);
        Assert.Equal("new", actions[3].Command.Text);
        Assert.Equal("private", actions[4].Command.Text);
    }

    [Fact]
    public void ActionSummariesRoundTripAndOldPayloadsStillParse()
    {
        var apps = IpcContract.ParseApps(IpcContract.Serialize(new[]
        {
            new AppSummary { Id = "browser.desktop", Actions = [new AppActionSummary("new", "New Window", "browser-new")] },
        }));
        Assert.Equal("new", Assert.Single(Assert.Single(apps).Actions).Id);
        Assert.Empty(Assert.Single(IpcContract.ParseApps("[{\"id\":\"old.desktop\"}]")).Actions);
    }
}
