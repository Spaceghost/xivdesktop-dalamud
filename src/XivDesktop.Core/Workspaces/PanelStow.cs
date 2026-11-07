using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Workspaces;

/// <summary>Temporary hiding remembers which panels were visible, without changing workspace membership.</summary>
public sealed class PanelStow
{
    private readonly Dictionary<long, bool> previousHidden = [];
    private readonly HashSet<long> preservedHidden = [];
    public bool PreservesHidden(long id) => preservedHidden.Contains(id);
    public void Reveal(long id) => preservedHidden.Remove(id);
    public bool Active { get; private set; }
    public bool HasEntries => previousHidden.Count != 0;
    public bool Owns(long id) => previousHidden.ContainsKey(id);

    /// <summary>Include world terminals, HUD and adopted panels when the full panel list is available.</summary>
    public static List<WindowPanel> WorldPanels(IReadOnlyList<WindowPanel> windows, PanelListSnapshot? panels)
    {
        var all = windows.Where(w => !w.IsEnded && w.Kind is "pet" or "pin" or "hud" or "full")
            .ToDictionary(w => w.Id, w => w.Kind == "full" ? w with { Kind = "pin" } : w);
        foreach (var p in panels?.Panels ?? [])
        {
            if (p.View is not ("pet" or "pin" or "hud" or "full" or "hidden")) continue;
            all[p.Id] = new WindowPanel
            {
                Id = p.Id, Title = p.Title, App = p.App, State = "live", Focused = p.Focused,
                Kind = p.View is "hidden" or "full" ? "pin" : p.View,
                Hidden = p.IsHidden,
            };
        }
        return all.Values.ToList();
    }

    /// <summary>Remembers new panels during a duty too. Exemptions can be changed while stowed.</summary>
    public List<VisibilityChange> Plan(IReadOnlyList<WindowPanel> windows, bool active,
        Func<WindowPanel, bool> hidden, Func<WindowPanel, bool> exempt)
    {
        Active = active;
        var live = windows.Where(w => !w.IsEnded && w.Kind is "pet" or "pin" or "hud").ToList();
        var ids = live.Select(w => w.Id).ToHashSet();
        preservedHidden.IntersectWith(ids);
        foreach (var w in live)
            if (!hidden(w)) preservedHidden.Remove(w.Id);
        foreach (var id in previousHidden.Keys.Where(id => !ids.Contains(id)).ToList())
            previousHidden.Remove(id);
        var changes = new List<VisibilityChange>();
        foreach (var w in live)
        {
            var isHidden = hidden(w);
            if (active && !exempt(w))
            {
                previousHidden.TryAdd(w.Id, isHidden);
                if (!isHidden)
                    changes.Add(new VisibilityChange(w.Id, true));
            }
            else if (previousHidden.TryGetValue(w.Id, out var before))
            {
                if (before != isHidden)
                    changes.Add(new VisibilityChange(w.Id, before));
                else
                {
                    if (before) preservedHidden.Add(w.Id);
                    previousHidden.Remove(w.Id);
                }
            }
        }
        return changes;
    }

    /// <summary>Restore entries are removed only once the backend has accepted the restoration.</summary>
    public void Restored(long id)
    {
        if (previousHidden.TryGetValue(id, out var before) && before) preservedHidden.Add(id);
        previousHidden.Remove(id);
    }
    public void Reset() { previousHidden.Clear(); preservedHidden.Clear(); Active = false; }
}

/// <summary>A short settle time on release avoids flashing panels between overlapping combat/duty conditions.</summary>
public sealed class StowLatch
{
    private long releaseAt = -1;
    public bool Active { get; private set; }
    public bool Update(bool requested, long nowMs, int settleMs = 500)
    {
        if (requested)
        {
            Active = true;
            releaseAt = -1;
        }
        else if (Active)
        {
            if (releaseAt < 0) releaseAt = nowMs;
            if (nowMs - releaseAt >= Math.Max(0, settleMs)) Active = false;
        }
        return Active;
    }
}
