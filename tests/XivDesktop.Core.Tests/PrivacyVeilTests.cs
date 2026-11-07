using XivDesktop.Core.Privacy;
using XivDesktop.Core.Windows;
using XivDesktop.Core.Workspaces;
using XivDesktop.Core.Palette;

namespace XivDesktop.Core.Tests;

public class PrivacyVeilTests
{
    [Fact]
    public void ManualVeilAndGroupPoseCombineWithoutOverwritingManualChoice()
    {
        Assert.False(PrivacyVeil.Active(false, false, true));
        Assert.True(PrivacyVeil.Active(false, true, true));
        Assert.False(PrivacyVeil.Active(false, true, false));
        Assert.True(PrivacyVeil.Active(true, false, true));
        Assert.True(PrivacyVeil.Active(true, true, false));
    }

    [Fact]
    public void WindowsKeepIdentityAndStateButLoseSensitiveDisplayMetadata()
    {
        var window = new WindowPanel { Id = 7, Title = "private document", App = "/home/secret/program", Key = "private key", Agent = "private host", State = "live", Kind = "pet", Hidden = false, Focused = true };
        var masked = PrivacyVeil.Mask(window, true);
        Assert.Equal("Window 7", masked.DisplayName);
        Assert.Equal("", masked.App);
        Assert.Equal("", masked.Key);
        Assert.Equal("", masked.Agent);
        Assert.Equal(window.Id, masked.Id);
        Assert.True(masked.IsLive);
        Assert.True(masked.Focused);
        Assert.Same(window, PrivacyVeil.Mask(window, false));
        Assert.Equal("private document", window.Title); // the source snapshot is never changed
    }

    [Fact]
    public void NativeTerminalAndAdoptedPanelMetadataAreMaskedToo()
    {
        var source = new PanelListSnapshot { Rev = 12, Native = true, Panels = [new PanelInfo { Id = 8, Kind = "terminal", Title = "secret title", App = "secret app", Profile = "secret profile", Icon = "/secret/path.png", View = "pin" }] };
        var masked = PrivacyVeil.Mask(source, true)!;
        Assert.Equal(source.Rev, masked.Rev);
        Assert.True(masked.Native);
        var panel = Assert.Single(masked.Panels);
        Assert.Equal("Panel 8", panel.DisplayName);
        Assert.Equal("", panel.Profile);
        Assert.Equal("", panel.Icon);
        Assert.Equal("", panel.App);
        Assert.Same(source, PrivacyVeil.Mask(source, false));
        Assert.Null(PrivacyVeil.Mask((PanelListSnapshot?)null, true));
    }

    [Fact]
    public void PaletteCannotMatchOrReturnHiddenWindowTitlesOrProfiles()
    {
        var windows = new[] { new WindowPanel { Id = 7, Title = "Secret diary", App = "private-app", Kind = "pet", State = "live" } };
        var ctx = new PaletteContext { Windows = windows.Select(w => PrivacyVeil.Mask(w, true)).ToList() };
        Assert.Empty(PaletteEngine.Build(ctx, "w:secret"));
        Assert.DoesNotContain(PaletteEngine.Build(ctx, "w:"), i => i.Title.Contains("Secret") || i.Subtitle.Contains("private"));
        var panelCtx = ctx with { Panels = PrivacyVeil.Mask(new PanelListSnapshot { Panels = [new PanelInfo { Id = 8, Title = "Secret notes", Profile = "secret-profile", View = "pin", Kind = "terminal" }] }, true) };
        Assert.Empty(PaletteEngine.Build(panelCtx, "w:secret"));
    }

    [Fact]
    public void PrivatePanelsRemainSuppressedWhenDutyGuardEndsOrAnAppIsExempt()
    {
        Assert.True(PrivacyVeil.Suppressed(true, false, false, false));
        Assert.False(PrivacyVeil.Suppressed(true, true, false, false));
        Assert.True(PrivacyVeil.Suppressed(true, true, true, true));
        Assert.True(PrivacyVeil.Suppressed(false, true, true, true));
        Assert.False(PrivacyVeil.Suppressed(false, false, false, true));
        var stow = new PanelStow();
        var visible = new WindowPanel { Id = 1, State = "live", Kind = "pet", Hidden = false };
        stow.Plan([visible], true, w => w.Hidden == true, _ => !PrivacyVeil.Suppressed(true, true, true, true));
        var hidden = visible with { Hidden = true };
        Assert.Empty(stow.Plan([hidden], true, w => w.Hidden == true, _ => !PrivacyVeil.Suppressed(false, true, true, true)));
        Assert.True(stow.Owns(1));
        Assert.Equal(new[] { new VisibilityChange(1, false) }, stow.Plan([hidden], false, w => w.Hidden == true, _ => false));
    }

    [Fact]
    public void PrivateTagsRebindByTitleAndAppAfterReconnect()
    {
        var mark = new PrivatePanelMark(1, "Notes", "editor");
        Assert.True(mark.Matches(new WindowPanel { Id = 1, Title = "Renamed", App = "editor" }));
        Assert.True(mark.Matches(new WindowPanel { Id = 100, Title = "Notes", App = "editor" }));
        Assert.False(mark.Matches(new WindowPanel { Id = 100, Title = "Other", App = "editor" }));
        Assert.False(mark.Matches(new WindowPanel { Id = 100, Title = "Notes", App = "other" }));
    }
}
