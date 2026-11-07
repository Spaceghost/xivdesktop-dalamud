using System.Text;

namespace XivDesktop.Core.Clipboard;

public sealed record ClipboardSample(string Text, bool Sensitive = false);
public sealed record ClipboardTransfer(ClipboardSample Game, ClipboardSample Host, string? ToGame, string? ToHost);

/// <summary>Text-only synchronization. The caller commits only after writes succeed.</summary>
public sealed class ClipboardBridge
{
    public const int MaxBytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private ClipboardSample? game;
    private ClipboardSample? host;

    public void Reset() { game = null; host = null; }

    public ClipboardTransfer? Observe(bool enabled, ClipboardSample currentGame, ClipboardSample currentHost)
    {
        if (!enabled || !Allowed(currentGame) || !Allowed(currentHost))
        {
            Reset();
            return null;
        }
        if (game is null || host is null)
        {
            game = currentGame;
            host = currentHost;
            return null; // Enabling never publishes an existing clipboard.
        }
        if (currentGame.Text == currentHost.Text)
        {
            game = currentGame;
            host = currentHost;
            return null;
        }
        var gameChanged = currentGame.Text != game.Text;
        var hostChanged = currentHost.Text != host.Text;
        // Concurrent changes are ambiguous: establish a fresh baseline, overwrite neither.
        if (gameChanged && hostChanged)
        {
            game = currentGame;
            host = currentHost;
            return null;
        }
        if (gameChanged) return new(currentGame, currentHost, null, currentGame.Text);
        if (hostChanged) return new(currentGame, currentHost, currentHost.Text, null);
        return null;
    }

    public void Commit(ClipboardTransfer transfer)
    {
        game = new(transfer.ToGame ?? transfer.Game.Text);
        host = new(transfer.ToHost ?? transfer.Host.Text);
    }

    private static bool Allowed(ClipboardSample sample)
    {
        if (sample.Sensitive || sample.Text.Contains('\0')) return false;
        try { return Utf8.GetByteCount(sample.Text) <= MaxBytes; }
        catch (EncoderFallbackException) { return false; }
    }
}
