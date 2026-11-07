using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using XivDesktop.Core;
using XivDesktop.Core.Windows;
using XivDesktop.Core.Workspaces;
using XivDesktop.Core.Privacy;
using XivDesktop.Shared;

namespace XivDesktop.Plugin.Services;

/// <summary>
/// The desktop session on top of the window list: which panel actions apply to, workspaces (hiding the
/// others' panels), focus cycling, pin/pet, the terminal key, and open/end notifications. Everything that
/// changes state runs on the framework thread; reads (<see cref="WindowsPayload"/>) are safe anywhere.
/// </summary>
public sealed partial class SessionService : IDisposable
{
    public const string HideArgs = "hide";
    public const string ShowArgs = "hide off";

    private readonly IWindowManager windows;
    private readonly GhosttyPostBackend post;
    private readonly DesktopService desktop;
    private readonly Configuration config;
    private readonly IFramework framework;
    private readonly INotificationManager notifications;
    private readonly IPluginLog log;
    private readonly WorkspaceModel workspaces;
    private readonly PanelStow stow = new();
    private bool stowed;
    private bool manualVeil;
    private bool gpose;
    public bool Veiled { get; private set; }
    public IReadOnlyList<WindowPanel> VisibleWindows => windows.Snapshot.Windows.Select(w => PrivacyVeil.Mask(w, Veiled)).ToList();
    public PanelListSnapshot? VisiblePanels => PrivacyVeil.Mask(windows.PanelSnapshot, Veiled);
    public string? VisibleMessage => Veiled && LastMessage != null ? "Privacy veil active." : LastMessage;
    private long lastStowPoll;
    private long lastFocused;
    private volatile string payloadJson = IpcContract.Serialize(new WindowsPayload());

    public SessionService(IWindowManager windows, GhosttyPostBackend post, DesktopService desktop, Configuration config, IFramework framework, INotificationManager notifications, IPluginLog log)
    {
        this.windows = windows;
        this.post = post;
        this.desktop = desktop;
        this.config = config;
        this.framework = framework;
        this.notifications = notifications;
        this.log = log;
        workspaces = new WorkspaceModel(config.Workspaces, config.CurrentWorkspace);
        windows.Changed += OnChanged;
        framework.Update += UpdatePlate;
        windows.RequestFailed += OnRequestFailed;
    }

    public IWindowManager Windows => windows;

    public int CurrentWorkspace => workspaces.Current;

    public int WorkspaceOf(long id) => workspaces.WorkspaceOf(id);

    public bool IsScratchpad(long id) => workspaces.IsScratchpad(id);

    public bool IsHidden(long id) => workspaces.Hidden.Contains(id);

    public string? LastMessage { get; private set; }

    /// <summary>Live and pending panels, oldest first.</summary>
    private List<WindowPanel> WorkspacePanels() => windows.Snapshot.Rev == -1 ? [] : Panels.WorkspaceWindows(windows.Snapshot, windows.PanelSnapshot);

    public List<WindowPanel> OpenPanels() => windows.Snapshot.Windows.Where(w => !w.IsEnded).ToList();

    /// <summary>Hidden by window.hide (as reported), or by XivDesktop's own place "hide" on an older ghostty.</summary>
    public bool IsHiddenNow(WindowPanel w) => w.Hidden ?? workspaces.Hidden.Contains(w.Id);

    /// <summary>The panel window actions apply to: the focused one, else the last one focused, else the newest on this workspace.</summary>
    public WindowPanel? Target()
    {
        var open = OpenPanels().Where(w => !IsHiddenNow(w)).ToList();
        return open.FirstOrDefault(w => w.Focused)
               ?? open.FirstOrDefault(w => w.Id == lastFocused)
               ?? open.LastOrDefault(w => workspaces.WorkspaceOf(w.Id) is var ws && (ws == 0 || ws == workspaces.Current));
    }

    public AppInfo? AppFor(WindowPanel w)
    {
        desktop.LaunchedApps.TryGetValue(w.Id, out var launched);
        return WindowApps.Find(desktop.Catalog.Catalog.Apps, w, launched ?? workspaces.AppIdOf(w.Id));
    }

    /// <summary>Notifications must never focus a panel based only on its editable title.</summary>
    public AppInfo? NotificationAppFor(WindowPanel w)
    {
        desktop.LaunchedApps.TryGetValue(w.Id, out var launched);
        return WindowApps.Find(desktop.Catalog.Catalog.Apps, w, launched ?? workspaces.AppIdOf(w.Id), allowTitleMatch: false);
    }

    // Actions (framework thread) ---------------------------------------------------------------

    public string Focus(long id)
    {
        if (SuppressedId(id)) return Report("error: the panel is hidden by the duty guard or privacy veil");
        stow.Reveal(id);
        if (workspaces.ShowScratchpad(id)) ApplyVisibility();
        var ws = workspaces.WorkspaceOf(id);
        if (ws != 0 && ws != workspaces.Current)
            SwitchWorkspace(ws); // sway: focusing a window on another workspace goes there
        var r = windows.Focus(id);
        if (r.StartsWith("ok", StringComparison.Ordinal))
            lastFocused = id;
        return Report(r);
    }

    public string Close(long id) => Report(windows.Close(id));

    /// <summary>
    /// Focus any panel. Window panels on another workspace switch there first. With panel.list the panel.focus
    /// method is used (terminals, chat and adopted windows too); else window.focus.
    /// </summary>
    public string FocusPanel(long id)
    {
        if (SuppressedId(id)) return Report("error: the panel is hidden by the duty guard or privacy veil");
        stow.Reveal(id);
        if (workspaces.ShowScratchpad(id)) ApplyVisibility();
        if (windows.PanelSnapshot == null)
            return Focus(id);
        var ws = workspaces.WorkspaceOf(id);
        if (ws != 0 && ws != workspaces.Current)
            SwitchWorkspace(ws);
        var r = windows.PanelChange("focus", id);
        if (r.StartsWith("ok", StringComparison.Ordinal))
            lastFocused = id;
        return Report(r);
    }

    /// <summary>
    /// A panel.* change; on an older ghostty the window.* equivalent where there is one (close, place,
    /// toggle_pet), else an error naming what is missing (minimize, order).
    /// </summary>
    public string PanelOp(string method, long id, string arg = "")
    {
        if (method is "place" or "toggle_pet" && SuppressedId(id))
            return Report("error: this panel is hidden by the duty guard or privacy veil");
        if (windows.PanelSnapshot != null)
        {
            var r = windows.PanelChange(method, id, arg);
            if (method == "place" && r.StartsWith("ok", StringComparison.Ordinal))
            {
                workspaces.MarkHidden(id, arg.Trim() == HideArgs);
                if (!arg.Trim().StartsWith("hide", StringComparison.OrdinalIgnoreCase)) rememberedPlacements[id] = arg;
            }
            return Report(r);
        }

        return method switch
        {
            "close" => Close(id),
            "place" => Place(id, arg),
            "toggle_pet" => windows.Snapshot.Find(id) is { } w ? TogglePet(w) : Report($"error: no window panel {id}"),
            _ => Report($"error: {method} needs ghostty-dalamud's panel IPC (panel.{method})"),
        };
    }

    public string Place(long id, string pin)
    {
        if (pin.Trim() != HideArgs && SuppressedId(id))
            return Report("error: this panel is hidden by the duty guard or privacy veil");
        var r = windows.Place(id, pin);
        if (r.StartsWith("ok", StringComparison.Ordinal))
        {
            if (pin.Trim().StartsWith("hide", StringComparison.OrdinalIgnoreCase)) rememberedPlacements.Remove(id);
            else rememberedPlacements[id] = pin;
            // A new anchor is shown whatever the old one was; keep the model in step.
            workspaces.MarkHidden(id, pin.Trim() == HideArgs);
            if (pin.Trim() != HideArgs) stow.Reveal(id);
            lastFocused = id;
        }

        return Report(r);
    }

    public string CloseTarget() => Target() is { } w ? Close(w.Id) : Report(NoTarget());

    /// <summary>
    /// Pet ↔ pin. With window.toggle_pet a pet is pinned right where it was last shown; an older ghostty gets
    /// window.place with <see cref="Configuration.PinArgs"/> or "pet".
    /// </summary>
    public string TogglePet() => Target() is { } w ? TogglePet(w) : Report(NoTarget());

    public string TogglePet(WindowPanel w)
    {
        if (ShouldSuppress(w)) return Report("error: this panel is hidden by the duty guard or privacy veil");
        if (!windows.Extended)
            return Place(w.Id, w.IsPet ? config.PinArgs : "pet");
        var r = windows.TogglePet(w.Id);
        if (r.StartsWith("ok", StringComparison.Ordinal))
            lastFocused = w.Id;
        return Report(r);
    }

    public string PinFront() => Target() is { } w ? Place(w.Id, config.PinFrontArgs) : Report(NoTarget());

    /// <summary>Focus the next (+1) or previous (-1) panel on the current workspace, wrapping around.</summary>
    public string CycleFocus(int step)
    {
        // ghostty's focus.cycle skips hidden panels, which with hiding on is exactly this workspace, and it
        // also cycles through world terminals.
        if (windows.Extended && config.WorkspacesHide && !stowed && !Veiled)
            return Report(windows.FocusCycle(step));

        var ids = workspaces.PanelsOn(workspaces.Current, windows.Snapshot.Windows);
        foreach (var w in OpenPanels())
        {
            if (workspaces.WorkspaceOf(w.Id) == 0 && !ids.Contains(w.Id))
                ids.Add(w.Id);
        }

        ids.RemoveAll(SuppressedId);
        ids.Sort();
        if (ids.Count == 0)
            return Report("error: no window panels on this workspace");
        var current = Target()?.Id ?? 0;
        var i = ids.IndexOf(current);
        var next = i < 0 ? (step > 0 ? ids[0] : ids[^1]) : ids[((i + step) % ids.Count + ids.Count) % ids.Count];
        return Focus(next);
    }

    public string SwitchWorkspace(int n)
    {
        if (n is < 1 or > WorkspaceModel.Count)
            return Report($"error: workspaces are 1..{WorkspaceModel.Count}");
        if (!workspaces.Switch(n))
            return Report($"ok: workspace {n}");
        Persist();
        ApplyVisibility();
        return Report($"ok: workspace {n}");
    }

    public bool Stowed => stowed;

    public bool IsPrivate(WindowPanel w)
        => config.PrivatePanels.Any(mark => mark.Matches(w))
            || AppFor(w) is { } app && UserLists.Contains(config.PrivateApps, app.Id);

    public bool IsTaggedPrivate(WindowPanel w) => config.PrivatePanels.Any(mark => mark.Matches(w));

    private bool SuppressedId(long id)
    {
        var panel = windows.Snapshot.Find(id) ?? PanelStow.WorldPanels(windows.Snapshot.Windows, windows.PanelSnapshot).FirstOrDefault(w => w.Id == id);
        return panel != null && ShouldSuppress(panel);
    }

    private bool ShouldSuppress(WindowPanel w)
        => PrivacyVeil.Suppressed(stowed, AppFor(w) is { } app && UserLists.Contains(config.StowExceptions, app.Id), Veiled, IsPrivate(w));

    public void TogglePrivate(WindowPanel panel)
    {
        var existing = config.PrivatePanels.Where(mark => mark.Matches(panel)).ToList();
        config.PrivatePanels = existing.Count > 0
            ? config.PrivatePanels.Except(existing).ToList()
            : [.. config.PrivatePanels, new PrivatePanelMark(panel.Id, panel.Title, panel.App)];
        desktop.Save();
        ApplyVisibility();
        RefreshPayload();
    }

    public void SetGpose(bool value)
    {
        gpose = value;
        UpdateVeil();
    }

    public string SetVeil(bool? value = null)
    {
        manualVeil = value ?? !manualVeil;
        UpdateVeil();
        return Report(Veiled ? "ok: privacy veil on" : "ok: privacy veil off");
    }

    private void UpdateVeil()
    {
        var active = PrivacyVeil.Active(manualVeil, gpose, config.VeilInGpose);
        if (active == Veiled) return;
        Veiled = active;
        LastMessage = null;
        ApplyVisibility();
        RefreshPayload();
    }

    public void SetStowed(bool value)
    {
        if (stowed == value)
        {
            // The full panel list can change without a window.list revision (e.g. a new terminal).
            if ((stowed || Veiled || stow.HasEntries) && Environment.TickCount64 - lastStowPoll >= 500)
            {
                lastStowPoll = Environment.TickCount64;
                ApplyVisibility();
            }
            return;
        }
        stowed = value;
        ApplyVisibility();
        RefreshPayload();
    }

    public string StashTarget()
    {
        if (Target() is not { } w) return Report(NoTarget());
        if (w.Kind is not ("pet" or "pin"))
        {
            var placed = Place(w.Id, config.ScratchpadPin);
            if (!placed.StartsWith("ok", StringComparison.Ordinal)) return placed;
        }
        workspaces.Move(w.Id, 0, w);
        workspaces.DismissScratchpad();
        Persist();
        var result = SetHidden(w.Id, true);
        if (result.StartsWith("ok", StringComparison.Ordinal)) workspaces.MarkHidden(w.Id, true);
        return Report(result.StartsWith("ok", StringComparison.Ordinal) ? "ok: stashed " + PrivacyVeil.Mask(w, Veiled).DisplayName : result);
    }

    public string SummonScratchpad()
    {
        if (stowed) return Report("error: the desk is stowed; summon the scratchpad after the duty or combat");
        var id = workspaces.SummonScratchpad(windows.Snapshot.Windows.Where(w => !SuppressedId(w.Id)).ToList());
        ApplyVisibility();
        if (id is not { } shown) return Report("ok: scratchpad hidden (stash a panel to add one)");
        var result = SetHidden(shown, false);
        if (!result.StartsWith("ok", StringComparison.Ordinal)) return Report(result);
        workspaces.MarkHidden(shown, false);
        return Report(windows.Focus(shown));
    }

    public string MoveTarget(int n) => Target() is { } w ? Move(w.Id, n) : Report(NoTarget());

    public string Move(long id, int n)
    {
        if (n is < 1 or > WorkspaceModel.Count)
            return Report($"error: workspaces are 1..{WorkspaceModel.Count}");
        var w = WorkspacePanels().FirstOrDefault(w => w.Id == id);
        if (w == null || w.IsEnded)
            return Report($"error: no window panel {id}");
        workspaces.Move(id, n, w);
        Persist();
        ApplyVisibility();
        return Report($"ok: {PrivacyVeil.Mask(w, Veiled).DisplayName} → workspace {n}");
    }

    /// <summary>Super+Enter: terminal.new (profile and pin from the settings); an older ghostty gets the /term line.</summary>
    public string OpenTerminal()
        => windows.Extended ? Report(windows.TerminalNew(config.TerminalProfile, config.TerminalPin)) : PostLine(config.TerminalLine);

    public string PostLine(string line)
    {
        if (!post.Available)
            return Report("error: " + post.MissingMessage);
        try
        {
            post.Post(line);
            return Report("ok: /term " + line);
        }
        catch (Exception ex)
        {
            return Report("error: " + ex.Message);
        }
    }

    /// <summary>Re-send hide/show for every panel (after turning hiding on or off).</summary>
    public void ResetVisibility()
    {
        foreach (var w in OpenPanels())
        {
            if (IsHiddenNow(w) && !config.WorkspacesHide && !stow.Owns(w.Id) && !ShouldSuppress(w) && !workspaces.IsScratchpad(w.Id))
            {
                SetHidden(w.Id, false);
                workspaces.MarkHidden(w.Id, false);
            }
        }

        ApplyVisibility();
    }

    /// <summary>window.hide on a ghostty that has it; else /term pin hide through window.place.</summary>
    private string SetHidden(long id, bool hide)
        => windows.Extended ? windows.Hide(id, hide) : windows.Place(id, hide ? HideArgs : ShowArgs);

    private string NoTarget() => windows.WindowsAvailable ? "error: no window panel to act on" : "error: needs ghostty-dalamud's window IPC (GhosttyDalamud.v1.Call)";

    private string Report(string result)
    {
        if (Veiled && result.StartsWith("error", StringComparison.Ordinal)) result = "error: the panel action failed; check the local log";
        LastMessage = result;
        RefreshPayload();
        if (result.StartsWith("error", StringComparison.Ordinal))
            log.Information("XivDesktop: {Result}", result);
        return result;
    }

    // Window list changes ----------------------------------------------------------------------

    private void OnChanged(WindowListSnapshot before, WindowListSnapshot after)
    {
        if (after.Rev == -1)
        {
            rememberedPlacements.Clear();
            stow.Reset();
            workspaces.ForgetHidden();
        } // ghostty went away: its anchors are gone with it

        if (after.Windows.FirstOrDefault(w => w.Focused) is { } focused)
            lastFocused = focused.Id;

        var workspacePanels = WorkspacePanels();
        var changed = workspaces.Reconcile(workspacePanels);
        foreach (var pair in desktop.LaunchedApps) changed |= workspaces.AssignApp(pair.Key, pair.Value);
        if (changed) Persist();
        ApplyVisibility();
        RefreshPayload();

        if (before.Rev == -1)
            return; // the first list after load is not news
        foreach (var e in WindowDiff.Diff(before, after))
            Notify(e);
        foreach (var id in desktop.LaunchedApps.Keys)
        {
            if (after.Find(id) == null && !workspacePanels.Any(p => p.Id == id && !p.IsEnded))
                desktop.LaunchedApps.TryRemove(id, out _);
        }
    }

    private void ApplyVisibility()
    {
        if (!windows.WindowsAvailable) return;
        var snapshot = windows.Snapshot.Windows;
        var restored = new HashSet<long>();
        foreach (var change in stow.Plan(PanelStow.WorldPanels(snapshot, windows.PanelSnapshot), stowed || Veiled, IsHiddenNow,
                     w => !ShouldSuppress(w)))
        {
            var r = SetHidden(change.Id, change.Hide);
            if (!r.StartsWith("ok", StringComparison.Ordinal)) return;
            workspaces.MarkHidden(change.Id, change.Hide);
            if (PanelStow.WorldPanels(snapshot, windows.PanelSnapshot).FirstOrDefault(w => w.Id == change.Id) is { } w && !ShouldSuppress(w))
            {
                stow.Restored(change.Id);
                restored.Add(change.Id);
            }
        }
        foreach (var change in workspaces.Plan(WorkspacePanels(), config.WorkspacesHide))
        {
            if (stow.Owns(change.Id) || restored.Contains(change.Id) || !change.Hide && stow.PreservesHidden(change.Id)) continue;
            var r = SetHidden(change.Id, change.Hide);
            if (r.StartsWith("ok", StringComparison.Ordinal))
                workspaces.MarkHidden(change.Id, change.Hide);
            else
                break; // queue full or ghostty gone: try again on the next change
        }
    }

    private void OnRequestFailed(WindowRequestResult r)
    {
        LastMessage = Veiled ? "error: a panel request failed; check the local Ghostty log" : $"error: {r.Method}: {r.Error}";
        if (r.Method == "window.open")
        {
            notifications.AddNotification(new Notification
            {
                Title = "Window did not open",
                Content = Veiled ? "A panel request failed. Check the local Ghostty log." : r.Error ?? "ghostty refused it",
                Type = NotificationType.Warning,
            });
        }
    }

    private void Notify(WindowEvent e)
    {
        var opened = e.Kind == WindowEventKind.Opened;
        if (opened ? !config.NotifyOpened : !config.NotifyEnded)
            return;
        try
        {
            var app = AppFor(e.Window);
            var n = new Notification
            {
                Title = Veiled ? opened ? "Panel opened" : "Panel closed" : opened ? e.Window.DisplayName : $"{e.Window.DisplayName} closed",
                Content = Veiled ? "A desktop panel changed while the privacy veil is active." : opened
                    ? $"Opened as a {(e.Window.Kind.Length > 0 ? e.Window.Kind : "panel")} on workspace {Math.Max(1, workspaces.WorkspaceOf(e.Window.Id))}" + (app != null && app.Name != e.Window.Title ? $" · {app.Name}" : "")
                    : "The window panel ended.",
                Type = NotificationType.Info,
                InitialDuration = TimeSpan.FromSeconds(opened ? 4 : 3),
                Minimized = !opened,
            };
            if (!Veiled && app?.IconPath is { } icon)
                n.Icon = INotificationIcon.FromFile(icon);
            else
                n.Icon = INotificationIcon.From(opened ? Dalamud.Interface.FontAwesomeIcon.WindowRestore : Dalamud.Interface.FontAwesomeIcon.WindowClose);
            notifications.AddNotification(n);
        }
        catch (Exception ex)
        {
            log.Debug(ex, "XivDesktop: notification failed");
        }
    }

    private void Persist()
    {
        config.Workspaces = workspaces.Export();
        config.CurrentWorkspace = workspaces.Current;
        desktop.Save();
    }

    // Reads (any thread) -----------------------------------------------------------------------

    /// <summary>The Windows IPC payload, rebuilt on the framework thread after every change; safe from any thread.</summary>
    public string PayloadJson => payloadJson;

    private void RefreshPayload()
    {
        try
        {
            payloadJson = IpcContract.Serialize(Payload());
        }
        catch (Exception ex)
        {
            log.Debug(ex, "XivDesktop: windows payload failed");
        }
    }

    /// <summary>Framework thread only (reads the workspace model).</summary>
    public WindowsPayload Payload()
    {
        var s = windows.Snapshot;
        return new WindowsPayload
        {
            Available = windows.WindowsAvailable,
            Veiled = Veiled,
            Rev = s.Rev,
            Workspace = workspaces.Current,
            Target = Target()?.Id,
            Windows = s.Windows.Select(w =>
            {
                var app = AppFor(w);
                return new WindowPayload
                {
                    Id = w.Id,
                    Title = PrivacyVeil.Mask(w, Veiled).Title,
                    App = Veiled ? "" : w.App,
                    State = w.State,
                    Kind = w.Kind,
                    Focused = w.Focused,
                    Workspace = workspaces.WorkspaceOf(w.Id),
                    Scratchpad = workspaces.IsScratchpad(w.Id),
                    Hidden = IsHiddenNow(w),
                    AppId = Veiled ? null : app?.Id,
                    IconPath = Veiled ? null : app?.IconPath,
                };
            }).ToList(),
        };
    }

    public void Dispose()
    {
        // Restore temporary duty hiding on unload; workspace visibility remains the player's grouping.
        foreach (var change in stow.Plan(PanelStow.WorldPanels(windows.Snapshot.Windows, windows.PanelSnapshot), false, IsHiddenNow, _ => false))
        {
            try { SetHidden(change.Id, change.Hide); }
            catch (Exception ex) { log.Debug(ex, "XivDesktop: restoring stowed panel failed"); }
        }
        framework.Update -= UpdatePlate;
        windows.Changed -= OnChanged;
        windows.RequestFailed -= OnRequestFailed;
    }
}
