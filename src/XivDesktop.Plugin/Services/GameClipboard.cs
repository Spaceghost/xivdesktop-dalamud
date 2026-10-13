using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using XivDesktop.Core.Clipboard;

namespace XivDesktop.Plugin.Services;

internal static class GameClipboard
{
    private static uint[]? formats;
    public static ClipboardSample? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            formats ??= new[] { "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory",
                "CanUploadToCloudClipboard", "x-kde-passwordManagerHint", "application/x-nspasteboard-concealed-type" }
                .Select(RegisterClipboardFormat).ToArray();
            if (formats.Any(f => f == 0)) return null;
            return ClipboardSampling.Read(GetClipboardSequenceNumber,
                () => formats.Any(IsClipboardFormatAvailable), () => ImGui.GetClipboardText() ?? "");
        }
        catch (Exception) { return null; }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClipboardFormatW")]
    private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);
}
