namespace XivDesktop.Core;

public sealed record SvgIconRequest(int Size, IReadOnlyList<string> Icons);
public sealed record SvgIconReply(string Icon, int Size, string Png);

/// <summary>Only icon metadata changes; commands and launch identity remain those of the scanned app.</summary>
public static class SvgIconRequests
{
    public const int BatchSize = 64;
    public static IReadOnlyList<SvgIconRequest> Build(IEnumerable<AppInfo> apps, int size)
    {
        if (size is < 16 or > 512) throw new ArgumentOutOfRangeException(nameof(size));
        return apps.Where(a => a.IconKind == IconKind.SvgOnly && ValidName(a.Icon))
            .Select(a => a.Icon).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Chunk(BatchSize).Select(names => new SvgIconRequest(size, names)).ToList();
    }
    public static AppCatalog ApplyCatalog(AppCatalog source, int size, IEnumerable<SvgIconReply> replies, Func<string, string?> toLocal)
    {
        var snapshot = replies.ToArray();
        IReadOnlyList<AppInfo> apps = source.Apps;
        foreach (var request in Build(source.Apps, size)) apps = Apply(apps, request, snapshot, toLocal);
        return new AppCatalog(apps, source.Stats with
        {
            IconsPng = apps.Count(a => a.IconKind == IconKind.Png),
            IconsSvgOnly = apps.Count(a => a.IconKind == IconKind.SvgOnly),
            IconsMissing = apps.Count(a => a.IconKind == IconKind.Missing),
        }, source.Errors, source.ScannedAt);
    }

    public static bool ValidName(string? icon) => !string.IsNullOrWhiteSpace(icon) && System.Text.Encoding.UTF8.GetByteCount(icon) <= 1024 && !icon.Any(char.IsControl);

    public static IReadOnlyList<AppInfo> Apply(IReadOnlyList<AppInfo> apps, SvgIconRequest request,
        IEnumerable<SvgIconReply> replies, Func<string, string?> toLocal)
    {
        var wanted = request.Icons.ToHashSet(StringComparer.Ordinal);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reply in replies)
        {
            if (reply.Size != request.Size || !wanted.Contains(reply.Icon) || paths.ContainsKey(reply.Icon)
                || !reply.Png.StartsWith('/') || !reply.Png.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || reply.Png.Any(char.IsControl)) continue;
            var local = toLocal(reply.Png);
            if (local != null) paths[reply.Icon] = local;
        }
        return apps.Select(a => a.IconKind == IconKind.SvgOnly && paths.TryGetValue(a.Icon, out var path)
            ? a with { IconKind = IconKind.Png, IconPath = path } : a).ToList();
    }
}
