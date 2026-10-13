using XivDesktop.Core.Diagnostics;
using XivDesktop.Core.Windows;

namespace XivDesktop.Core.Tests;

public class DesktopDoctorTests
{
    private static DoctorEvidence Healthy => new(42, false, 250, true, true,
        new AgentStatus(true, 1, true, "secret-host-name"), true, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ReadinessNeverClaimsAnUntestedAppWasLaunched()
    {
        var report = DesktopDoctor.Build(Healthy);
        Assert.All(report.Steps.Take(5), s => Assert.Equal(DoctorVerdict.Ready, s.Verdict));
        Assert.Equal(DoctorVerdict.NotRun, report.Steps.Last().Verdict);
        Assert.DoesNotContain("secret-host-name", report.RedactedText());
    }

    [Fact]
    public void MissingLayersHaveSeparateVerdictsAndUsefulNextSteps()
    {
        var report = DesktopDoctor.Build(Healthy with { Apps = 0, LaunchGate = false, WindowGate = false, Agent = null, FocusPolled = false });
        Assert.Equal(DoctorVerdict.Failed, report.Steps[0].Verdict);
        Assert.Contains("reload", report.Steps[0].Fix);
        Assert.Equal(DoctorVerdict.Failed, report.Steps[1].Verdict);
        Assert.Equal(DoctorVerdict.Failed, report.Steps[2].Verdict);
        Assert.Equal(DoctorVerdict.Waiting, report.Steps[3].Verdict);
        Assert.Equal(DoctorVerdict.Waiting, report.Steps[4].Verdict);
        Assert.Equal(DoctorVerdict.Waiting, DesktopDoctor.Build(Healthy with { Apps = 0, Scanning = true }).Steps[0].Verdict);
        Assert.Equal(DoctorVerdict.Failed, DesktopDoctor.Build(Healthy with { Agent = new AgentStatus(true, 1, false, "") }).Steps[3].Verdict);
    }

    [Fact]
    public void OnlyTheMatchingRequestAndItsLivePanelPass()
    {
        var probe = new DoctorLaunchProbe(10, 0);
        probe.Update(new WindowListSnapshot { Requests = [new WindowRequestResult { Request = 99, Ok = true, PanelId = 7 }], Windows = [new WindowPanel { Id = 7, State = "live" }] }, 1);
        Assert.False(probe.Finished);
        probe.Update(new WindowListSnapshot { Requests = [new WindowRequestResult { Request = 10, Ok = true, PanelId = 7 }], Windows = [new WindowPanel { Id = 7, State = "pending" }] }, 2);
        Assert.False(probe.Finished);
        // Request history can expire while the compositor is still starting the panel.
        probe.Update(new WindowListSnapshot { Windows = [new WindowPanel { Id = 7, State = "live", Title = "secret window title", App = "secret command" }] }, 3);
        Assert.True(probe.Finished);
        Assert.Equal(DoctorVerdict.Ready, probe.Step.Verdict);
        var report = DesktopDoctor.Build(Healthy, probe.Step).RedactedText();
        Assert.DoesNotContain("secret", report);
    }

    [Fact]
    public void FailureTextCannotLeakIntoCopiedReports()
    {
        var probe = new DoctorLaunchProbe(1, 0);
        probe.Update(new WindowListSnapshot { Requests = [new WindowRequestResult { Request = 1, Ok = false, Error = "/home/user/private/password.txt" }] }, 1);
        Assert.Equal(DoctorVerdict.Failed, probe.Step.Verdict);
        Assert.DoesNotContain("/home", DesktopDoctor.Build(Healthy, probe.Step).RedactedText());
    }

    [Fact]
    public void EndedPanelsAndMissingResponsesFailAndTerminalResultsStayTerminal()
    {
        var probe = new DoctorLaunchProbe(1, 0);
        probe.Update(new WindowListSnapshot { Requests = [new WindowRequestResult { Request = 1, Ok = true, PanelId = 7 }], Windows = [new WindowPanel { Id = 7, State = "ended" }] }, 1);
        Assert.Equal(DoctorVerdict.Failed, probe.Step.Verdict);
        probe.Update(new WindowListSnapshot { Requests = [new WindowRequestResult { Request = 1, Ok = true, PanelId = 7 }], Windows = [new WindowPanel { Id = 7, State = "live" }] }, 2);
        Assert.Equal(DoctorVerdict.Failed, probe.Step.Verdict);
        var missing = new DoctorLaunchProbe(1, 100);
        missing.Update(WindowListSnapshot.Empty, 100 + DoctorLaunchProbe.TimeoutMs);
        Assert.Equal(DoctorVerdict.Failed, missing.Step.Verdict);
    }
}
