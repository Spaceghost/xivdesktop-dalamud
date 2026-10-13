using Dalamud.Plugin.Services;
using XivDesktop.Core.Layouts;

namespace XivDesktop.Plugin.Services;

public sealed partial class SessionService
{
    private readonly Dictionary<long, string> rememberedPlacements = [];
    private Queue<PlatePlanPiece>? plateQueue;
    private PlatePlanPiece? platePiece;
    private long? platePanel;
    private long plateStarted;
    private long plateReadyAt;
    private PlateRequest? plateRequest;
    private int platePhase;
    private DeskPlate? applyingPlate;
    public IReadOnlyList<DeskPlate> Plates => config.Plates;
    public bool ContextBusy => applyingPlate != null || stowed || Veiled;
    public uint CurrentTerritory { get; set; }
    public string? PlateStatus { get; private set; }

    public void TerritoryTransition()
    {
        if (applyingPlate != null) CancelPlate("error: plate interrupted by a territory transition; already applied pieces are kept");
    }

    public string ZoneCommand(string text)
    {
        if (Veiled) return Report("error: lift the privacy veil before managing zone layouts");
        var split = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var action = split.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        var value = split.Length > 1 ? split[1].Trim() : "";
        if (action == "list") return Report("ok: zone layouts: " + string.Join(", ", config.ZoneLayouts.OrderBy(p => p.Key)
            .Select(p => $"{p.Key}: {(p.Value.Workspace > 0 ? "workspace " + p.Value.Workspace : "plate " + p.Value.Plate)}{(p.Value.AllowInDuty ? " (duties allowed)" : "")}")));
        if (CurrentTerritory == 0) return Report("error: enter a territory before binding its layout");
        if (action == "clear")
        {
            config.ZoneLayouts.Remove(CurrentTerritory);
            desktop.Save();
            return Report("ok: removed this territory's layout");
        }
        config.ZoneLayouts.TryGetValue(CurrentTerritory, out var old);
        ZoneLayoutRule rule;
        if (action == "ws" && int.TryParse(value, out var workspace)) rule = new(Workspace: workspace, AllowInDuty: old?.AllowInDuty ?? false);
        else if (action == "plate" && config.Plates.Any(p => p.Name.Equals(value, StringComparison.OrdinalIgnoreCase))) rule = new(value, AllowInDuty: old?.AllowInDuty ?? false);
        else if (action == "duties" && old != null && value is "on" or "off") rule = old with { AllowInDuty = value == "on" };
        else return Report("error: usage: /desktop zone ws <1-9> | plate <name> | clear | duties on|off | list");
        if (!rule.Valid) return Report("error: invalid zone layout");
        config.ZoneLayouts[CurrentTerritory] = rule;
        desktop.Save();
        return Report($"ok: bound territory {CurrentTerritory}; applies after loading settles");
    }

    public string PlateCommand(string text)
    {
        if (Veiled) return Report("error: lift the privacy veil before managing plates");
        var split = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = split.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        var name = split.Length > 1 ? split[1].Trim() : "";
        return verb switch
        {
            "list" => Report("ok: plates: " + string.Join(", ", Plates.Select(p => p.Name))),
            "save" => SavePlate(name),
            "apply" => ApplyPlate(name),
            "delete" => DeletePlate(name),
            "cancel" => CancelPlate("cancelled; already applied pieces are kept"),
            "status" => Report(PlateStatus ?? "ok: no plate has been applied"),
            _ => ApplyPlate(text.Trim()),
        };
    }

    public string SavePlate(string name)
    {
        if (Veiled || stowed) return Report("error: lift the privacy veil and duty guard before saving a plate");
        var pieces = new List<PlatePiece>();
        foreach (var w in WorkspacePanels().Where(w => w.IsLive && !IsScratchpad(w.Id)))
        {
            var app = AppFor(w);
            if (app == null && windows.Snapshot.Find(w.Id) == null) continue; // native shells and adopted UI without a desktop app are not plate pieces
            if (app == null) return Report("error: a panel has no catalog app; close it before saving this plate");
            var pin = w.SupportsPlacementCapture ? (w.Pin.Length > 0 ? w.Pin : null)
                : rememberedPlacements.GetValueOrDefault(w.Id) ?? (w.IsPet ? "pet" : null);
            if (pin == null && w.SupportsPlacementCapture) return Report("error: a fixed pin is in another territory or has an unsupported anchor; place it here before saving");
            if (pin == null) return Report("error: set each pinned panel's placement through XivDesktop before saving; update Ghostty to capture full placement arguments");
            var order = windows.PanelSnapshot?.Panels.FirstOrDefault(p => p.Id == w.Id)?.Order;
            pieces.Add(new PlatePiece(app.Id, Math.Max(1, WorkspaceOf(w.Id)), pin, order is > 0 ? order.Value - 1 : pieces.Count));
        }
        var plate = new DeskPlate(name, pieces, CurrentWorkspace);
        if (PlatePlanner.Validate(plate) is { } error) return Report("error: " + error);
        config.Plates.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        config.Plates.Add(plate);
        desktop.Save();
        return Report($"ok: saved plate {name} ({pieces.Count} apps)");
    }

    public string DeletePlate(string name)
    {
        if (config.Plates.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == 0)
            return Report("error: no such plate");
        desktop.Save();
        return Report("ok: deleted plate " + name);
    }

    public string ApplyPlate(string name)
    {
        if (applyingPlate != null) return Report("error: another plate is applying; use /desktop plate cancel first");
        if (stowed || Veiled) return Report("error: lift the privacy veil and duty guard before applying a plate");
        var plate = Plates.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (plate == null) return Report("error: no such plate");
        if (PlatePlanner.Validate(plate) is { } error) return Report("error: " + error);
        if (!windows.WindowsAvailable || windows.PanelSnapshot == null)
            return Report("error: plates need ghostty-dalamud's window and panel IPC for placement and ordering");
        foreach (var piece in plate.Pieces)
        {
            var app = desktop.Catalog.Catalog.Find(piece.AppId);
            if (app == null || !desktop.CanLaunch(app, out _)) return Report("error: a plate app is unavailable: " + piece.AppId);
            if (config.PrivateApps.Contains(app.Id)) return Report("error: remove private apps from this plate before applying");
        }
        applyingPlate = plate;
        plateQueue = new Queue<PlatePlanPiece>(PlatePlanner.Build(plate, WorkspacePanels().Where(w => !IsPrivate(w)), w => AppFor(w)?.Id));
        platePiece = null;
        PlateStatus = "ok: applying plate " + plate.Name;
        return Report(PlateStatus);
    }

    private string CancelPlate(string reason)
    {
        applyingPlate = null;
        plateQueue = null;
        platePiece = null;
        plateRequest = null;
        PlateStatus = reason.StartsWith("error:", StringComparison.Ordinal) ? reason : "ok: " + reason;
        return Report(PlateStatus);
    }

    private void UpdatePlate(IFramework _)
    {
        if (applyingPlate == null) return;
        try
        {
            if (stowed || Veiled) { CancelPlate("error: plate interrupted by duty guard or privacy veil"); return; }
            var now = Environment.TickCount64;
            if (platePiece == null)
            {
                if (!plateQueue!.TryDequeue(out platePiece))
                {
                    var name = applyingPlate.Name;
                    SwitchWorkspace(applyingPlate.Workspace);
                    CancelPlate("applied plate " + name);
                    return;
                }
                platePanel = platePiece.PanelId;
                plateStarted = now;
                plateReadyAt = now;
                platePhase = 0;
                if (platePanel == null)
                {
                    var generation = applyingPlate;
                    var piece = platePiece;
                    var app = desktop.Catalog.Catalog.Find(piece.Piece.AppId)!;
                    var result = desktop.Launch(app, id =>
                    {
                        // Late callbacks from a cancelled application must not affect a later plate.
                        if (ReferenceEquals(applyingPlate, generation) && ReferenceEquals(platePiece, piece))
                        {
                            platePanel = id;
                            plateReadyAt = Environment.TickCount64 + (app.Terminal ? DesktopService.TerminalSendDelayMs : 0);
                        }
                    });
                    if (!result.StartsWith("ok", StringComparison.Ordinal)) { CancelPlate(result); return; }
                }
            }
            if (platePanel is not { } panel || WorkspacePanels().FirstOrDefault(w => w.Id == panel) is not { IsLive: true })
            {
                if (now - plateStarted >= PlateRequest.TimeoutMs) CancelPlate("error: timed out waiting for a plate app to become live");
                return;
            }
            if (now < plateReadyAt) return;
            if (SuppressedId(panel)) { CancelPlate("error: plate panel became private"); return; }
            if (plateRequest != null)
            {
                plateRequest.Update(windows.Snapshot, now);
                if (plateRequest.Error != null) { CancelPlate("error: " + plateRequest.Error); return; }
                if (!plateRequest.Complete) return;
                plateRequest = null;
                platePhase++;
            }
            string response;
            if (platePhase == 0)
            {
                // Ordering only works on shown pets: visit each piece's workspace before placing it.
                SwitchWorkspace(platePiece.Piece.Workspace);
                var moved = Move(panel, platePiece.Piece.Workspace);
                if (!moved.StartsWith("ok", StringComparison.Ordinal)) { CancelPlate(moved); return; }
                response = windows.Snapshot.Find(panel) != null ? Place(panel, platePiece.Piece.Pin)
                    : PanelOp("place", panel, platePiece.Piece.Pin);
            }
            else if (platePhase == 1 && platePiece.Piece.Pin.Trim().Split(' ')[0].Equals("pet", StringComparison.OrdinalIgnoreCase))
                response = windows.PanelChange("order", panel, (platePiece.Piece.Order + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            else { platePiece = null; return; }
            if (!PlateRequest.TryParse(response, out var request)) { CancelPlate("error: placement or ordering was not queued: " + response); return; }
            plateRequest = new PlateRequest(request, now, false);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "XivDesktop: applying a plate failed");
            CancelPlate("error: applying the plate failed; already applied pieces are kept");
        }
    }
}
