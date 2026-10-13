namespace XivDesktop.Core.Layouts;

public sealed record ZoneLayoutRule(string Plate = "", int Workspace = 0, bool AllowInDuty = false)
{
    public bool Valid => (Workspace is >= 1 and <= 9 && string.IsNullOrWhiteSpace(Plate))
        || (Workspace == 0 && !string.IsNullOrWhiteSpace(Plate) && Plate.Length <= 80 && !Plate.Any(char.IsControl));
}

/// <summary>One application per arrival or rule edit, after loading settles. Busy guards postpone it.</summary>
public sealed class ZoneLayoutTracker
{
    private uint territory;
    private ZoneLayoutRule? rule;
    private long readySince = -1;
    private bool applied;
    public void Invalidate() { territory = 0; readySince = -1; applied = false; }

    public ZoneLayoutRule? Update(uint current, ZoneLayoutRule? selected, bool loaded, bool inDuty, bool busy, long now)
    {
        if (current != territory || selected != rule)
        {
            territory = current;
            rule = selected;
            readySince = -1;
            applied = false;
        }
        if (!loaded || current == 0)
        {
            readySince = -1;
            // A loading transition into the same territory is still a new arrival.
            applied = false;
            return null;
        }
        if (applied || rule is not { Valid: true }) return null;
        if (readySince < 0) readySince = now;
        if (busy || inDuty && !rule.AllowInDuty || now - readySince < 1000) return null;
        applied = true;
        return rule;
    }
}
