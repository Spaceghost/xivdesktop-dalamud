namespace XivDesktop.Core.Clipboard;

public static class ClipboardSampling
{
    public static ClipboardSample? Read(Func<uint> sequence, Func<bool> sensitive, Func<string> text)
    {
        var before = sequence();
        if (before == 0 || sensitive()) return null;
        var value = text();
        if (sequence() != before || sensitive()) return null;
        return new(value);
    }
}
