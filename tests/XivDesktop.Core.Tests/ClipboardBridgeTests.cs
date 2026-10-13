using XivDesktop.Core.Clipboard;

namespace XivDesktop.Core.Tests;

public class ClipboardBridgeTests
{
    [Fact]
    public void EnableBaselinesExistingTextThenRelaysBothDirectionsWithoutEcho()
    {
        var bridge = new ClipboardBridge();
        Assert.Null(bridge.Observe(true, new("game"), new("host")));
        var copy = bridge.Observe(true, new("copied"), new("host"))!;
        Assert.Equal("copied", copy.ToHost);
        Assert.Null(copy.ToGame);
        bridge.Commit(copy);
        Assert.Null(bridge.Observe(true, new("copied"), new("copied")));
        copy = bridge.Observe(true, new("copied"), new("notes"))!;
        Assert.Equal("notes", copy.ToGame);
        bridge.Commit(copy);
        Assert.Null(bridge.Observe(true, new("notes"), new("notes")));
    }

    [Fact]
    public void UncommittedWriteRetriesAndConcurrentChangesOverwriteNeither()
    {
        var bridge = new ClipboardBridge();
        bridge.Observe(true, new("a"), new("b"));
        Assert.Equal("c", bridge.Observe(true, new("c"), new("b"))!.ToHost);
        Assert.Equal("c", bridge.Observe(true, new("c"), new("b"))!.ToHost);
        Assert.Null(bridge.Observe(true, new("c"), new("d")));
        Assert.Null(bridge.Observe(true, new("c"), new("d")));
    }

    [Theory]
    [InlineData("secret", true)]
    [InlineData("bad\0text", false)]
    public void SensitiveOrInvalidSamplesResetTheBaseline(string text, bool sensitive)
    {
        var bridge = new ClipboardBridge();
        bridge.Observe(true, new("a"), new("b"));
        Assert.Null(bridge.Observe(true, new("changed"), new(text, sensitive)));
        Assert.Null(bridge.Observe(true, new("changed"), new("safe")));
    }

    [Fact]
    public void Utf8ByteCapAndDisablePreventTransfer()
    {
        var bridge = new ClipboardBridge();
        bridge.Observe(true, new("a"), new("b"));
        Assert.Null(bridge.Observe(true, new(new string('界', ClipboardBridge.MaxBytes / 3 + 1)), new("b")));
        Assert.Null(bridge.Observe(true, new("a"), new("b")));
        Assert.Null(bridge.Observe(false, new("changed"), new("b")));
        Assert.Null(bridge.Observe(true, new("changed"), new("b")));
    }
}
