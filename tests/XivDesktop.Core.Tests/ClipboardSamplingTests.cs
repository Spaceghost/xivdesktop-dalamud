using XivDesktop.Core.Clipboard;

namespace XivDesktop.Core.Tests;

public class ClipboardSamplingTests
{
    [Fact]
    public void SensitiveClipboardIsNeverRead()
    {
        Assert.Null(ClipboardSampling.Read(() => 1, () => true,
            () => throw new InvalidOperationException("must not read")));
    }

    [Fact]
    public void MissingAccessIsNeverRead()
    {
        Assert.Null(ClipboardSampling.Read(() => 0, () => false,
            () => throw new InvalidOperationException("must not read")));
    }

    [Fact]
    public void ChangedContentsOrLateSensitiveHintDiscardTheSample()
    {
        uint seq = 1;
        Assert.Null(ClipboardSampling.Read(() => seq, () => false, () => { seq++; return "changed"; }));
        var sensitive = false;
        Assert.Null(ClipboardSampling.Read(() => 1, () => sensitive, () => { sensitive = true; return "private"; }));
    }

    [Fact]
    public void StableUnmarkedTextIsEligibleForThePlanner()
    {
        Assert.Equal(new ClipboardSample("notes"), ClipboardSampling.Read(() => 1, () => false, () => "notes"));
    }
}
