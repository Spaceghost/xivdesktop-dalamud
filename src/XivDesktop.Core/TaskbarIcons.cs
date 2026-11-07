namespace XivDesktop.Core;

/// <summary>Shared bounded cache of decoded-valid icon files; missing files are cached too.</summary>
public sealed class TaskbarIconCache(Func<string, byte[]?> read, Func<byte[], bool> valid, int capacity = 128)
{
    private readonly Dictionary<string, (byte[]? Bytes, long ReadAt, long Used)> entries = new(StringComparer.Ordinal);
    private long sequence;
    public byte[]? Get(string? path, long now)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (entries.TryGetValue(path, out var entry) && now - entry.ReadAt < 30_000)
        {
            entries[path] = entry with { Used = ++sequence };
            return entry.Bytes;
        }
        byte[]? bytes;
        try
        {
            bytes = read(path);
            if (bytes != null && entry.Bytes != null && bytes.AsSpan().SequenceEqual(entry.Bytes)) bytes = entry.Bytes;
            else if (bytes != null && !valid(bytes)) bytes = null;
        }
        catch { bytes = null; }
        if (!entries.ContainsKey(path) && entries.Count >= Math.Max(1, capacity))
            entries.Remove(entries.MinBy(p => p.Value.Used).Key);
        entries[path] = (bytes, now, ++sequence);
        return bytes;
    }
    public void Clear() => entries.Clear();
}

/// <summary>The first snapshot is baseline; only newly live windows pulse for four seconds.</summary>
public sealed class TaskbarArrivals
{
    private Dictionary<long, string> previous = [];
    private readonly Dictionary<long, long> opened = [];
    private bool initialized;
    public void Update(IEnumerable<(long Id, string State)> panels, bool veiled, long now)
    {
        var next = panels.Where(p => p.State != "ended").ToDictionary(p => p.Id, p => p.State);
        foreach (var id in opened.Keys.Where(id => !next.ContainsKey(id)).ToList()) opened.Remove(id);
        if (veiled) opened.Clear();
        else if (initialized)
            foreach (var pair in next)
                if (pair.Value == "live" && (!previous.TryGetValue(pair.Key, out var state) || state != "live")) opened[pair.Key] = now;
        previous = next;
        initialized = true;
    }
    public bool Highlight(long id, long now) => opened.TryGetValue(id, out var start) && now - start < 4000;
}
