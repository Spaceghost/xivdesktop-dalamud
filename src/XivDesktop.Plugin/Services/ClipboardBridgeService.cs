using Dalamud.Bindings.ImGui;
using XivDesktop.Core.Clipboard;

namespace XivDesktop.Plugin.Services;

/// <summary>UI-thread adapter for the tested synchronization workflow.</summary>
public sealed class ClipboardBridgeService : IClipboardTransport
{
    private readonly Configuration config;
    private readonly GhosttyCallBackend backend;
    private readonly SessionService session;
    private readonly ClipboardSync sync;
    public ClipboardBridgeService(Configuration config, GhosttyCallBackend backend, SessionService session)
    {
        this.config = config; this.backend = backend; this.session = session;
        sync = new ClipboardSync(this);
    }
    public void Tick() => sync.Tick(config.ClipboardBridgeEnabled && !session.Veiled && !session.Stowed && backend.WindowsAvailable && backend.Agent is { Connected: true }, Environment.TickCount64);
    public long? QueueRead() => backend.QueueClipboard(false);
    public long? QueueWrite(string text) => backend.QueueClipboard(true, text);
    public ClipboardQueuedResult? QueuedResult(long id)
    {
        var result = backend.Snapshot.Requests.FirstOrDefault(r => r.Request == id);
        return result == null ? null : new(result.Ok, result.ClipboardRequest);
    }
    public ClipboardResult? Snapshot() => backend.ReadClipboardResult();
    public ClipboardSample? ReadGame() => GameClipboard.Read();
    public void WriteGame(string text) => ImGui.SetClipboardText(text);
}
