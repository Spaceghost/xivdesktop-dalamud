using System.Numerics;

namespace XivDesktop.Core;

public sealed record ControllerRingResult(string? LaunchId = null, int WorkspaceStep = 0);

/// <summary>Hold a user-selected chord, flick toward a favourite, then release to launch.</summary>
public sealed class ControllerRing
{
    public const int Capacity = 8;
    public bool Open { get; private set; }
    public int Selected { get; private set; } = -1;
    public IReadOnlyList<AppInfo> Apps { get; private set; } = [];
    private bool wasHeld;
    private bool leftDown;
    private bool rightDown;
    private bool cancelled;

    public ControllerRingResult Update(bool chordHeld, Vector2 stick, bool leftShoulder, bool rightShoulder,
        bool cancel, bool allowed, IEnumerable<AppInfo> favourites, float deadzone = 0.35f)
    {
        if (!allowed || cancel)
        {
            Close();
            cancelled = chordHeld;
            wasHeld = chordHeld;
            leftDown = leftShoulder;
            rightDown = rightShoulder;
            return new();
        }
        if (!chordHeld)
        {
            var launch = Open && Selected >= 0 ? Apps[Selected].Id : null;
            Close();
            cancelled = false;
            wasHeld = false;
            leftDown = leftShoulder;
            rightDown = rightShoulder;
            return new(launch);
        }
        if (!wasHeld && !cancelled)
        {
            Apps = favourites.DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Take(Capacity).ToArray();
            Open = Apps.Count > 0;
            Selected = -1;
        }
        wasHeld = true;
        if (Open)
        {
            var sector = Sector(stick, Apps.Count, deadzone);
            if (sector >= 0) Selected = sector; // retain the flick when the stick springs back before release
        }
        var step = Open ? (rightShoulder && !rightDown ? 1 : 0) - (leftShoulder && !leftDown ? 1 : 0) : 0;
        leftDown = leftShoulder;
        rightDown = rightShoulder;
        return new(WorkspaceStep: step);
    }

    public void Close()
    {
        Open = false;
        Selected = -1;
        Apps = [];
    }

    /// <summary>Clockwise sectors starting at up; input coordinates use positive Y for up.</summary>
    public static int Sector(Vector2 stick, int count, float deadzone = 0.35f)
    {
        if (count is < 1 or > Capacity || !float.IsFinite(stick.X) || !float.IsFinite(stick.Y)
            || !float.IsFinite(deadzone) || deadzone is < 0 or > 1 || stick.LengthSquared() <= deadzone * deadzone) return -1;
        var angle = MathF.Atan2(stick.X, stick.Y);
        if (angle < 0) angle += MathF.Tau;
        return (int)MathF.Floor(angle / MathF.Tau * count + 0.5f) % count;
    }
}
