using XivDesktop.Core.Windows;
using System.Text.Json;

namespace XivDesktop.Core.Tests;

public class ClipboardWireTests
{
    private static string Reply(string fields) => "{\"ok\":true,\"result\":{\"request\":42,\"operation\":\"read\",\"pending\":false,\"available\":true,\"text\":\"notes\"" + fields + "}}";

    [Fact]
    public void LegacyOrIncompleteSensitivityMetadataCannotSynchronize()
    {
        Assert.False(GhosttyWire.ParseClipboard(Reply(""))!.CanSynchronize);
        Assert.False(GhosttyWire.ParseClipboard(Reply(",\"sensitivity_known\":true"))!.CanSynchronize);
        Assert.False(GhosttyWire.ParseClipboard(Reply(",\"sensitivity_known\":true,\"sensitive\":true"))!.CanSynchronize);
        Assert.True(GhosttyWire.ParseClipboard(Reply(",\"sensitivity_known\":true,\"sensitive\":false"))!.CanSynchronize);
    }

    [Fact]
    public void WriteSerializationPreservesUnicodeAndRejectsOversizedText()
    {
        using var parsed = JsonDocument.Parse(GhosttyWire.ClipboardWrite("界\nnotes"));
        Assert.Equal("界\nnotes", parsed.RootElement.GetProperty("params").GetProperty("text").GetString());
        Assert.Throws<ArgumentException>(() => GhosttyWire.ClipboardWrite("bad\0text"));
        Assert.Throws<ArgumentException>(() => GhosttyWire.ClipboardWrite(new string('界', 22000)));
    }

    [Theory]
    [InlineData("{\"ok\":false}")]
    [InlineData("{\"ok\":true,\"result\":{}}")]
    [InlineData("broken")]
    public void InvalidRepliesAreUnavailable(string json) => Assert.Null(GhosttyWire.ParseClipboard(json));
}
