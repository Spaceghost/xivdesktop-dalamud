using Dalamud.Plugin.Services;
using XivDesktop.Core;
using XivDesktop.Core.Diagnostics;

namespace XivDesktop.Plugin.Services;

/// <summary>Capture immutable poll results on the framework thread; build the diagnostic report off it.</summary>
public sealed class DoctorService : IDisposable
{
    private readonly DesktopService desktop;
    private readonly IWindowManager windows;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private DoctorLaunchProbe? probe;
    private volatile DoctorReport? report;
    private volatile bool disposed;
    private int generation;

    public DoctorService(DesktopService desktop, IWindowManager windows, IFramework framework, IPluginLog log)
    {
        this.desktop = desktop; this.windows = windows; this.framework = framework; this.log = log;
        framework.Update += Update;
    }
    public DoctorReport? Report => report;
    public bool Probing => probe is { Finished: false };
    public string? LastMessage { get; private set; }

    public void Refresh()
    {
        var capture = new DoctorEvidence(desktop.Catalog.Catalog.Apps.Count, desktop.Catalog.Scanning,
            desktop.Catalog.LastScanMs, desktop.Backend.Available, windows.WindowsAvailable,
            windows.Agent, windows.KeyboardFocus != null, DateTimeOffset.UtcNow);
        var launch = probe?.Step;
        var mine = Interlocked.Increment(ref generation);
        _ = Task.Run(() =>
        {
            try
            {
                var next = DesktopDoctor.Build(capture, launch);
                if (!disposed && mine == Volatile.Read(ref generation)) report = next;
            }
            catch (Exception ex) { log.Debug(ex, "XivDesktop: doctor report failed"); }
        });
    }

    public void TestLaunch(string appId)
    {
        if (Probing) { LastMessage = "A launch test is already running."; return; }
        var app = desktop.Catalog.Catalog.Find(appId);
        if (app == null) { LastMessage = "Choose an installed app first."; return; }
        var plan = LaunchPlan.Plan(app);
        if (!windows.WindowsAvailable || plan.Target == null || plan.Target.Kind == LaunchKind.Terminal)
        { LastMessage = "Choose a graphical app; the test needs Ghostty's window Call gate."; return; }
        var result = plan.Target.Kind == LaunchKind.AgentApp
            ? windows.Open(match: XivDesktop.Core.Windows.GhosttyWire.AppMatch(plan.Target.Value), onPanel: id => desktop.LaunchedApps[id] = app.Id)
            : windows.Open(run: plan.Target.Value, onPanel: id => desktop.LaunchedApps[id] = app.Id);
        const string prefix = "ok: queued ";
        if (!result.StartsWith(prefix, StringComparison.Ordinal) || !long.TryParse(result.AsSpan(prefix.Length), out var request))
        { LastMessage = "The launch was not queued. Check the backend status."; return; }
        probe = new DoctorLaunchProbe(request, Environment.TickCount64);
        LastMessage = "Launch test queued. Leave the app open until the test observes its panel.";
        Refresh();
    }

    private void Update(IFramework _)
    {
        if (probe is not { Finished: false }) return;
        var before = probe.Step;
        probe.Update(windows.Snapshot, Environment.TickCount64);
        if (before != probe.Step) Refresh();
    }

    public void Dispose() { disposed = true; framework.Update -= Update; }
}
