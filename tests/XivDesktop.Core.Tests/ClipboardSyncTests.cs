using XivDesktop.Core.Clipboard;
namespace XivDesktop.Core.Tests;

public class ClipboardSyncTests
{
    private sealed class Transport : IClipboardTransport
    {
        public long Id;
        public string Game = "game";
        public string Host = "host";
        public bool Known = true, Sensitive, RejectWrite, ThrowRead;
        public int GameWrites, HostWrites;
        public string? Sent;
        public long? QueueRead() { Sent = null; return ++Id; }
        public long? QueueWrite(string text) { Sent = text; HostWrites++; return ++Id; }
        public ClipboardQueuedResult? QueuedResult(long id) => new(true, id * 10);
        public ClipboardResult? Snapshot() => new(Id * 10, Sent == null ? "read" : "write", false,
            !RejectWrite, Host, Known, Sensitive);
        public ClipboardSample? ReadGame() => ThrowRead ? throw new InvalidOperationException("clipboard owner unavailable") : new(Game);
        public void WriteGame(string text) { Game = text; GameWrites++; }
    }
    private static void Poll(ClipboardSync sync, long time) { sync.Tick(true, time); sync.Tick(true, time + 1); }

    [Fact]
    public void EnableBaselinesThenCopiesBothWaysWithoutEcho()
    {
        var t = new Transport(); var sync = new ClipboardSync(t);
        Poll(sync, 0);
        Assert.Equal(0, t.GameWrites);
        t.Host = "host copy"; Poll(sync, 1100);
        Assert.Equal("host copy", t.Game);
        t.Game = "game copy"; Poll(sync, 2200);
        Assert.Equal("game copy", t.Sent);
        t.Host = t.Sent!; sync.Tick(true, 2202);
        Poll(sync, 3300);
        Assert.Equal(1, t.HostWrites);
        Assert.Equal(1, t.GameWrites);
    }

    [Fact]
    public void UnknownAndSensitiveSnapshotsNeverWrite()
    {
        var t = new Transport(); var sync = new ClipboardSync(t);
        Poll(sync, 0);
        t.Host = "private"; t.Known = false; Poll(sync, 1100);
        Assert.Equal("game", t.Game);
        t.Known = true; t.Sensitive = true; Poll(sync, 2200);
        Assert.Equal(0, t.GameWrites); Assert.Equal(0, t.HostWrites);
    }

    [Fact]
    public void PauseDiscardsPendingReplyAndResumeBaselines()
    {
        var t = new Transport(); var sync = new ClipboardSync(t);
        Poll(sync, 0);
        t.Host = "copy"; sync.Tick(true, 1100);
        sync.Tick(false, 1101);
        sync.Tick(true, 1102);
        Assert.Equal(0, t.GameWrites);
        Poll(sync, 2200);
        Assert.Equal(0, t.GameWrites);
    }

    [Fact]
    public void FailedWriteDoesNotEstablishAnEchoBaseline()
    {
        var t = new Transport(); var sync = new ClipboardSync(t);
        Poll(sync, 0); t.Game = "copy"; Poll(sync, 1100);
        t.RejectWrite = true; sync.Tick(true, 1102);
        t.RejectWrite = false; Poll(sync, 2200);
        Assert.Equal(0, t.GameWrites);
        Assert.Equal("copy", t.Game);
    }
    [Fact]
    public void ReadExceptionResetsTheWorkflowAndResumesWithoutPublishingOldData()
    {
        var t = new Transport(); var sync = new ClipboardSync(t);
        Poll(sync, 0); t.Host = "new host"; t.ThrowRead = true;
        Poll(sync, 1100);
        Assert.Equal(0, t.GameWrites);
        t.ThrowRead = false;
        Poll(sync, 2200);
        Assert.Equal(0, t.GameWrites);
        Assert.Equal("game", t.Game);
    }

}
