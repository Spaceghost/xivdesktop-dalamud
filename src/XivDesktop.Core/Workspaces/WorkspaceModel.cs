using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Workspaces;

/// <summary>
/// A window panel's workspace membership, as persisted. Panel ids do not survive a ghostty reload or a
/// reconnect, so the title and app are kept too: an entry whose panel is gone is re-bound to a new panel
/// with the same title (or, failing that, the same app). Best effort by design.
/// </summary>
public sealed class WorkspaceEntry
{
    public int Workspace { get; set; } = 1;

    public long PanelId { get; set; }

    public string Title { get; set; } = "";

    public string App { get; set; } = "";
    public string? DesktopAppId { get; set; }
}

/// <summary>One visibility change to send: hide or show panel <paramref name="Id"/>.</summary>
public sealed record VisibilityChange(long Id, bool Hide);

/// <summary>
/// Nine numbered workspaces, sway style. Each window panel belongs to exactly one; the current workspace's
/// panels are shown and every other panel is hidden. Pure: callers turn <see cref="Plan"/> into
/// <c>window.place</c> calls and report back with <see cref="MarkHidden"/>.
/// </summary>
public sealed class WorkspaceModel
{
    public const int Count = 9;

    /// <summary>At most this many entries are kept for panels that are gone (for re-binding after a reconnect).</summary>
    public const int MaxOrphans = 32;

    private readonly List<WorkspaceEntry> entries;
    private readonly HashSet<long> hidden = [];

    public WorkspaceModel(IEnumerable<WorkspaceEntry>? entries = null, int current = 1)
    {
        this.entries = entries?.Where(e => e != null).Select(Clamp).ToList() ?? [];
        Current = Math.Clamp(current, 1, Count);
    }

    public int Current { get; private set; }

    public long? ShownScratchpad { get; private set; }

    public bool IsScratchpad(long id) => entries.Any(e => e.PanelId == id && e.Workspace == 0);

    public void DismissScratchpad() => ShownScratchpad = null;

    public bool ShowScratchpad(long id)
    {
        if (!IsScratchpad(id)) return false;
        ShownScratchpad = id;
        return true;
    }

    public long? SummonScratchpad(IReadOnlyList<WindowPanel> windows)
    {
        var ids = windows.Where(w => !w.IsEnded && IsScratchpad(w.Id)).Select(w => w.Id).ToList();
        if (ids.Count == 0 || (ids.Count == 1 && ShownScratchpad == ids[0]))
            return ShownScratchpad = null;
        var index = ShownScratchpad is { } shown ? ids.IndexOf(shown) : -1;
        return ShownScratchpad = ids[(index + 1) % ids.Count];
    }

    /// <summary>Copies of the entries, for persisting.</summary>
    public List<WorkspaceEntry> Export() => entries.Select(e => new WorkspaceEntry { Workspace = e.Workspace, PanelId = e.PanelId, Title = e.Title, App = e.App, DesktopAppId = e.DesktopAppId }).ToList();

    public IReadOnlyCollection<long> Hidden => hidden;

    public string? AppIdOf(long panelId) => entries.FirstOrDefault(e => e.PanelId == panelId)?.DesktopAppId;
    public bool AssignApp(long panelId, string appId)
    {
        var entry = entries.FirstOrDefault(e => e.PanelId == panelId);
        if (entry == null || entry.DesktopAppId == appId) return false;
        entry.DesktopAppId = appId;
        return true;
    }

    /// <summary>The workspace of a panel, or 0 when it is not known yet.</summary>
    public int WorkspaceOf(long panelId)
    {
        foreach (var e in entries)
        {
            if (e.PanelId == panelId)
                return e.Workspace;
        }

        return 0;
    }

    /// <summary>Panel ids on workspace <paramref name="n"/> among <paramref name="windows"/>, in list order.</summary>
    public List<long> PanelsOn(int n, IReadOnlyList<WindowPanel> windows)
        => windows.Where(w => !w.IsEnded && WorkspaceOf(w.Id) == n).Select(w => w.Id).ToList();

    /// <summary>
    /// Brings the entries in line with the current panels: known panels keep their workspace (and their title
    /// is refreshed), gone panels' entries are re-bound by title then app, and new panels join the current
    /// workspace. Returns true when the persisted state changed.
    /// </summary>
    public bool Reconcile(IReadOnlyList<WindowPanel> windows)
    {
        var changed = false;
        var present = new HashSet<long>(windows.Select(w => w.Id));
        hidden.IntersectWith(present);
        if (ShownScratchpad is { } shown && !windows.Any(w => w.Id == shown && !w.IsEnded))
            ShownScratchpad = null;

        foreach (var w in windows)
        {
            if (w.IsEnded)
                continue;
            var entry = entries.FirstOrDefault(e => e.PanelId == w.Id);
            if (entry == null)
            {
                entry = FindOrphan(present, e => w.Title.Length > 0 && e.Title == w.Title)
                        ?? FindOrphan(present, e => w.Title.Length > 0 && e.Title.Length == 0 && w.App.Length > 0 && e.App == w.App)
                        ?? FindOrphan(present, e => w.App.Length > 0 && e.App == w.App && e.Title.Length > 0 && w.Title.Length > 0 && SameStem(e.Title, w.Title));
                if (entry != null)
                {
                    entry.PanelId = w.Id;
                }
                else
                {
                    entry = new WorkspaceEntry { Workspace = Current, PanelId = w.Id };
                    entries.Add(entry);
                }

                changed = true;
            }

            if (w.Title.Length > 0 && entry.Title != w.Title)
            {
                entry.Title = w.Title;
                changed = true;
            }

            if (w.App.Length > 0 && entry.App != w.App)
            {
                entry.App = w.App;
                changed = true;
            }
        }

        // Ended panels' entries become orphans; keep only the newest few.
        var orphans = entries.Where(e => !present.Contains(e.PanelId) || windows.Any(w => w.Id == e.PanelId && w.IsEnded)).ToList();
        foreach (var o in orphans)
        {
            if (windows.Any(w => w.Id == o.PanelId && w.IsEnded))
            {
                // An ended panel will not come back under this id; 0 marks it as re-bindable.
                o.PanelId = 0;
                changed = true;
            }
        }

        var excess = orphans.Count - MaxOrphans;
        for (var i = 0; i < entries.Count && excess > 0; i++)
        {
            if (orphans.Contains(entries[i]))
            {
                entries.RemoveAt(i--);
                excess--;
                changed = true;
            }
        }

        return changed;
    }

    private WorkspaceEntry? FindOrphan(HashSet<long> present, Func<WorkspaceEntry, bool> match)
    {
        // Newest first: the most recent panel with that title is the likeliest one.
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            if (!present.Contains(e.PanelId) && match(e))
                return e;
        }

        return null;
    }

    // "notes.txt - Text Editor" and "other.txt - Text Editor": same app window, a different document.
    private static bool SameStem(string a, string b)
    {
        var ia = a.LastIndexOf(" - ", StringComparison.Ordinal);
        var ib = b.LastIndexOf(" - ", StringComparison.Ordinal);
        return ia > 0 && ib > 0 && a[ia..] == b[ib..];
    }

    /// <summary>Switches the current workspace. Returns false for an out-of-range number or no change.</summary>
    public bool Switch(int n)
    {
        if (n is < 1 or > Count || n == Current)
            return false;
        Current = n;
        return true;
    }

    /// <summary>Moves a panel to workspace <paramref name="n"/>. Returns false when out of range or already there.</summary>
    public bool Move(long panelId, int n, WindowPanel? window = null)
    {
        if (n is < 0 or > Count)
            return false;
        var entry = entries.FirstOrDefault(e => e.PanelId == panelId);
        if (entry == null)
        {
            entries.Add(new WorkspaceEntry { Workspace = n, PanelId = panelId, Title = window?.Title ?? "", App = window?.App ?? "" });
            return true;
        }

        if (entry.Workspace == n)
            return false;
        entry.Workspace = n;
        return true;
    }

    /// <summary>
    /// The hide/show changes needed for the current workspace. Only world panels (pet or pin) are hidden;
    /// full-screen and tab views are left alone. A panel's reported <see cref="WindowPanel.Hidden"/> wins over
    /// what <see cref="MarkHidden"/> recorded.
    /// </summary>
    public List<VisibilityChange> Plan(IReadOnlyList<WindowPanel> windows, bool hideWorkspaces = true)
    {
        var plan = new List<VisibilityChange>();
        foreach (var w in windows)
        {
            if (w.IsEnded || w.Kind is not ("pet" or "pin" or "hud"))
                continue;
            var ws = WorkspaceOf(w.Id);
            var scratchpad = IsScratchpad(w.Id);
            if (!scratchpad && !hideWorkspaces)
                continue;
            var wantHidden = scratchpad ? ShownScratchpad != w.Id : ws != 0 && ws != Current;

            // A ghostty-dalamud that reports "hidden" is the truth; an older one only has what we sent.
            var isHidden = w.Hidden ?? hidden.Contains(w.Id);
            if (wantHidden != isHidden)
                plan.Add(new VisibilityChange(w.Id, wantHidden));
        }

        return plan;
    }

    /// <summary>Records that a hide/show was sent (or that a re-placement showed the panel again).</summary>
    public void MarkHidden(long panelId, bool isHidden)
    {
        if (isHidden)
            hidden.Add(panelId);
        else
            hidden.Remove(panelId);
    }

    /// <summary>Forget what was hidden (after ghostty reloads, every anchor is new and shown).</summary>
    public void ForgetHidden() => hidden.Clear();

    private static WorkspaceEntry Clamp(WorkspaceEntry e)
    {
        e.Workspace = e.Workspace == 0 ? 0 : Math.Clamp(e.Workspace, 1, Count);
        e.Title ??= "";
        e.App ??= "";
        return e;
    }
}
