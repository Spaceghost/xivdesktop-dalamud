using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Diagnostics;

public enum DoctorVerdict { Ready, Waiting, Failed, NotRun }
public sealed record DoctorStep(string Name, DoctorVerdict Verdict, string Evidence, string Fix = "");
public sealed record DoctorEvidence(int Apps, bool Scanning, double? ScanMs, bool LaunchGate,
    bool WindowGate, AgentStatus? Agent, bool FocusPolled, DateTimeOffset At);
public sealed record DoctorReport(DateTimeOffset At, IReadOnlyList<DoctorStep> Steps)
{
    // Only generated evidence is included: never window titles, app commands, paths, addresses, or account details.
    public string RedactedText() => "XivDesktop doctor — " + At.ToString("O") + "\n"
        + string.Join("\n", Steps.Select(s => $"{s.Name}: {s.Verdict} — {s.Evidence}" + (s.Fix.Length > 0 ? $"\n  Next: {s.Fix}" : "")));
}

public static class DesktopDoctor
{
    public static DoctorReport Build(DoctorEvidence e, DoctorStep? launch = null)
    {
        var steps = new List<DoctorStep>
        {
            new("App catalog", e.Apps > 0 ? DoctorVerdict.Ready : e.Scanning ? DoctorVerdict.Waiting : DoctorVerdict.Failed,
                $"{e.Apps} apps; scan {(e.Scanning ? "running" : "idle")}" + (e.ScanMs is { } ms ? $"; last scan {ms:F0} ms" : ""),
                e.Apps > 0 ? "" : e.Scanning ? "Wait for the catalog scan to finish." : "Run /desktop reload; check Settings → General for the Linux home directory."),
            new("Launch IPC", e.LaunchGate ? DoctorVerdict.Ready : DoctorVerdict.Failed,
                e.LaunchGate ? "A Ghostty launch gate is registered." : "Neither usable Ghostty launch route is registered.",
                e.LaunchGate ? "" : "Enable Ghostty for Dalamud and reload XivDesktop."),
            new("Window IPC", e.WindowGate ? DoctorVerdict.Ready : DoctorVerdict.Failed,
                e.WindowGate ? "The Ghostty Call gate is registered." : "Window listing and control are unavailable.",
                e.WindowGate ? "" : "Install a Ghostty build with GhosttyDalamud.v1.Call."),
            new("Host agent", e.Agent is null ? DoctorVerdict.Waiting : e.Agent.Connected && e.Agent.WindowsOk ? DoctorVerdict.Ready : DoctorVerdict.Failed,
                e.Agent is null ? "No agent status has arrived." : $"Connected: {e.Agent.Connected}; window backend: {e.Agent.WindowsOk}; protocol: {e.Agent.Version}.",
                e.Agent is { Connected: true, WindowsOk: true } ? "" : "Start ghostty-agent on the Linux host with its Wayland window backend and wait for the next status poll."),
            new("Focus IPC", e.FocusPolled ? DoctorVerdict.Ready : DoctorVerdict.Waiting,
                e.FocusPolled ? "focus.get returned a valid response (no focused panel is also valid)." : "No valid focus.get response is available.",
                e.FocusPolled ? "" : "Wait for polling; if this persists, update Ghostty to a build with focus.get."),
            launch ?? new("Launch test", DoctorVerdict.NotRun, "No app was launched.", "Choose a harmless installed app and run the optional launch test."),
        };
        return new DoctorReport(e.At, steps);
    }
}

/// <summary>Queued is not success: require both the matching request result and a live panel.</summary>
public sealed class DoctorLaunchProbe
{
    private readonly long request;
    private readonly long startedMs;
    private long? resolvedPanel;
    public const int TimeoutMs = 20000;
    public DoctorStep Step { get; private set; } = new("Launch test", DoctorVerdict.Waiting, "Waiting for the queued request result.");
    public bool Finished => Step.Verdict != DoctorVerdict.Waiting;

    public DoctorLaunchProbe(long request, long nowMs) { this.request = request; startedMs = nowMs; }
    public void Update(WindowListSnapshot snapshot, long nowMs)
    {
        if (Finished) return;
        var result = snapshot.Requests.FirstOrDefault(r => r.Request == request);
        if (result is { Ok: false })
            Step = new("Launch test", DoctorVerdict.Failed, "The matching launch request failed.", "Check the Ghostty log locally; this redacted report omits its error text.");
        else
        {
            if (result is { Ok: true, PanelId: { } id }) resolvedPanel = id;
            if (resolvedPanel is { } panelId && snapshot.Find(panelId) is { } panel)
            {
                if (panel.IsLive)
                    Step = new("Launch test", DoctorVerdict.Ready, "The matching launch request resolved and its panel became live.", "Confirm that you can see and use the app in game; a live panel alone does not certify rendering.");
                else if (panel.IsEnded)
                    Step = new("Launch test", DoctorVerdict.Failed, "The created panel ended before it became live.", "Check the installed app and host compositor.");
            }
        }
        if (!Finished && nowMs - startedMs >= TimeoutMs)
            Step = new("Launch test", DoctorVerdict.Failed, "No matching live panel appeared within 20 seconds.", "Check the host app and compositor; a queued request is not launch success.");
    }
}
