namespace XivDesktop.Core.Input;

/// <summary>Capture one fresh supported key press, ignoring keys held when recording started.</summary>
public sealed class ChordCapture
{
    private readonly HashSet<int> heldAtStart = [];
    private long startedAt;
    public bool Active { get; private set; }
    public string Message { get; private set; } = "";
    public KeyMods ObservedModifiers { get; private set; }
    public const int TimeoutMs = 10000;

    public void Begin(Func<int, bool> down, long nowMs)
    {
        heldAtStart.Clear();
        for (var key = 8; key < 256; key++)
            if (!KeyChord.IsModifierKey(key) && down(key)) heldAtStart.Add(key);
        startedAt = nowMs;
        Active = true;
        ObservedModifiers = KeyMods.None;
        Message = "Press the chord now; Escape cancels. Recording lasts ten seconds.";
    }

    public void Cancel(string reason = "Recording cancelled.")
    {
        Active = false;
        Message = reason;
    }

    public KeyChord? Update(Func<int, bool> down, long nowMs)
    {
        if (!Active) return null;
        var mods = KeyChord.ModsFrom(down);
        ObservedModifiers |= mods;
        if (nowMs - startedAt >= TimeoutMs)
        {
            Cancel(ObservedModifiers == KeyMods.None
                ? "No chord received. No modifiers reached the game; check your desktop shortcuts or try Alt+Shift."
                : "No chord received. Modifiers reached the game, but no supported key was pressed.");
            return null;
        }
        heldAtStart.RemoveWhere(key => !down(key));
        for (var key = 8; key < 256; key++)
        {
            if (heldAtStart.Contains(key) || KeyChord.IsModifierKey(key) || !down(key)) continue;
            if (key == 0x1B && mods == KeyMods.None) { Cancel(); return null; }
            var chord = new KeyChord(mods, key);
            if (!KeyChord.TryParse(chord.ToString(), out _)) continue;
            Active = false;
            Message = "Recorded " + chord;
            return chord;
        }
        return null;
    }
}
