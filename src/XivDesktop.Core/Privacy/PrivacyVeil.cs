using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Privacy;

/// <summary>A saved private-panel tag: rebind by title/app after a Ghostty reconnect.</summary>
public sealed record PrivatePanelMark(long PanelId, string Title, string App)
{
    public bool Matches(WindowPanel panel) => panel.Id == PanelId
        || Title.Length > 0 && panel.Title == Title && panel.App == App;
}

public static class PrivacyVeil
{
    public static bool Active(bool manual, bool inGpose, bool autoGpose) => manual || inGpose && autoGpose;
    public static WindowPanel Mask(WindowPanel window, bool active) => !active ? window : window with
    {
        Title = "Window " + window.Id,
        App = "",
        Key = "",
        Agent = "",
    };
    public static PanelListSnapshot? Mask(PanelListSnapshot? panels, bool active) => !active || panels == null ? panels : panels with
    {
        Panels = panels.Panels.Select(p => p with { Title = "Panel " + p.Id, App = "", Profile = "", Icon = "" }).ToList(),
    };

    public static bool Suppressed(bool stowed, bool stowException, bool veiled, bool privatePanel)
        => stowed && !stowException || veiled && privatePanel;
}
