using XivDesktop.Core.Windows;
using XivDesktop.Core.Workspaces;

namespace XivDesktop.Core.Layouts;

public sealed record PlatePiece(string AppId, int Workspace, string Pin, int Order);
public sealed record DeskPlate(string Name, IReadOnlyList<PlatePiece> Pieces, int Workspace = 1);
public sealed record PlatePlanPiece(PlatePiece Piece, long? PanelId);

/// <summary>Assigns each plate piece a distinct live panel; unmatched pieces must be launched.</summary>
public static class PlatePlanner
{
    public static string? Validate(DeskPlate plate)
    {
        if (string.IsNullOrWhiteSpace(plate.Name) || plate.Name.Length > 80 || plate.Name.Any(char.IsControl))
            return "plate name must be 1..80 characters without control characters";
        if (plate.Workspace is < 1 or > WorkspaceModel.Count) return "plate workspace must be 1..9";
        if (plate.Pieces.Count is < 1 or > 32) return "a plate must contain 1..32 apps";
        foreach (var p in plate.Pieces)
        {
            if (string.IsNullOrWhiteSpace(p.AppId) || p.AppId.Any(char.IsControl)) return "each piece needs an app id";
            if (p.Workspace is < 1 or > WorkspaceModel.Count) return "piece workspace must be 1..9";
            if (string.IsNullOrWhiteSpace(p.Pin) || p.Pin.Length > 512 || p.Pin.Any(char.IsControl)) return "each piece needs placement arguments without control characters";
            if (p.Order < 0) return "piece order cannot be negative";
        }
        return null;
    }

    public static IReadOnlyList<PlatePlanPiece> Build(DeskPlate plate, IEnumerable<WindowPanel> windows, Func<WindowPanel, string?> appOf)
    {
        var candidates = windows.Where(w => w.IsLive).OrderBy(w => w.Id).ToList();
        var used = new HashSet<long>();
        return plate.Pieces.OrderBy(p => p.Order).Select(p =>
        {
            var match = candidates.FirstOrDefault(w => !used.Contains(w.Id) && string.Equals(appOf(w), p.AppId, StringComparison.OrdinalIgnoreCase));
            if (match != null) used.Add(match.Id);
            return new PlatePlanPiece(p, match?.Id);
        }).ToList();
    }
}

/// <summary>Correlates queued commands with their actual outcomes, retaining resolved panel ids as history expires.</summary>
public sealed class PlateRequest
{
    public const long TimeoutMs = 30_000;
    private readonly long request;
    private readonly long started;
    private readonly bool needsLivePanel;
    private long? panel;
    private bool accepted;
    public PlateRequest(long request, long started, bool needsLivePanel) => (this.request, this.started, this.needsLivePanel) = (request, started, needsLivePanel);
    public long? PanelId => panel;
    public string? Error { get; private set; }
    public bool Complete { get; private set; }
    public void Update(WindowListSnapshot snapshot, long now)
    {
        if (Complete || Error != null) return;
        var result = snapshot.Requests.FirstOrDefault(r => r.Request == request);
        if (result != null)
        {
            if (!result.Ok) { Error = result.Error ?? "queued operation failed"; return; }
            accepted = true;
            panel ??= result.PanelId;
        }
        if (accepted && !needsLivePanel) { Complete = true; return; }
        if (panel is { } id && snapshot.Find(id) is { } window)
        {
            if (window.IsEnded) Error = "launched panel ended before becoming live";
            else if (window.IsLive) Complete = true;
        }
        if (!Complete && Error == null && now - started >= TimeoutMs) Error = "timed out waiting for the queued operation";
    }
    public static bool TryParse(string response, out long request)
    {
        const string prefix = "ok: queued ";
        request = 0;
        return response.StartsWith(prefix, StringComparison.Ordinal) && long.TryParse(response.AsSpan(prefix.Length), out request) && request > 0;
    }
}
