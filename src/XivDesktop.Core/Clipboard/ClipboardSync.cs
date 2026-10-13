namespace XivDesktop.Core.Clipboard;

public sealed record ClipboardQueuedResult(bool Ok, long? ClipboardRequest);
public interface IClipboardTransport
{
    long? QueueRead();
    long? QueueWrite(string text);
    ClipboardQueuedResult? QueuedResult(long id);
    ClipboardResult? Snapshot();
    ClipboardSample? ReadGame();
    void WriteGame(string text);
}

public sealed class ClipboardSync(IClipboardTransport transport)
{
    private readonly ClipboardBridge bridge = new();
    private long? queued;
    private long? request;
    private long started;
    private long nextPoll;
    private bool writing;
    private ClipboardTransfer? transfer;

    private void Reset(long now)
    {
        bridge.Reset(); queued = null; request = null; transfer = null;
        nextPoll = now + 1000;
    }

    public void Tick(bool enabled, long now)
    {
        try { Step(enabled, now); }
        catch (Exception) { Reset(now); }
    }

    private void Step(bool enabled, long now)
    {
        if (!enabled)
        { Reset(now); return; }
        if (queued is null)
        {
            if (now < nextPoll) return;
            writing = false;
            queued = transport.QueueRead();
            started = now;
            if (queued is null) Reset(now);
            return;
        }
        if (now - started > 10000) { Reset(now); return; }
        if (request is null)
        {
            var result = transport.QueuedResult(queued.Value);
            if (result is null) return;
            if (!result.Ok || result.ClipboardRequest is not > 0) { Reset(now); return; }
            request = result.ClipboardRequest;
        }
        var clipboard = transport.Snapshot();
        if (clipboard is null || clipboard.Request != request || clipboard.Pending) return;
        var gameSample = transport.ReadGame();
        if (gameSample is null) { Reset(now); return; }
        var game = gameSample.Text;
        if (writing)
        {
            if (clipboard.WriteAcknowledged && transfer is { } copy && game == copy.Game.Text)
                bridge.Commit(copy);
            else bridge.Reset();
            transfer = null;
        }
        else
        {
            if (!clipboard.CanSynchronize) { Reset(now); return; }
            var copy = bridge.Observe(true, gameSample, new(clipboard.Text));
            if (copy?.ToGame is { } toGame)
            {
                if (transport.ReadGame() != gameSample) { Reset(now); return; }
                transport.WriteGame(toGame);
                if (transport.ReadGame()?.Text == toGame) bridge.Commit(copy);
                else bridge.Reset();
            }
            else if (copy?.ToHost is { } toHost)
            {
                if (transport.ReadGame() != gameSample) { Reset(now); return; }
                transfer = copy;
                writing = true;
                queued = transport.QueueWrite(toHost);
                request = null;
                started = now;
                if (queued is null) Reset(now);
                return;
            }
        }
        queued = null; request = null; nextPoll = now + 1000;
    }
}
