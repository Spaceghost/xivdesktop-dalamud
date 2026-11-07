using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using XivDesktop.Core.Diagnostics;
using XivDesktop.Plugin.Services;

namespace XivDesktop.Plugin.Windows;

public sealed class DoctorWindow : Window
{
    private readonly DoctorService doctor;
    private readonly DesktopService desktop;
    private string selected = "";
    public DoctorWindow(DoctorService doctor, DesktopService desktop) : base("XivDesktop doctor###XivDesktopDoctor")
    {
        this.doctor = doctor; this.desktop = desktop;
        Size = new Vector2(650, 560); SizeCondition = ImGuiCond.FirstUseEver;
    }
    public void Show() { doctor.Refresh(); IsOpen = true; BringToFront(); }
    public override void Draw()
    {
        if (ImGui.Button("Refresh checks")) doctor.Refresh();
        ImGui.SameLine();
        if (doctor.Report is { } report && ImGui.Button("Copy redacted report")) ImGui.SetClipboardText(report.RedactedText());
        ImGui.TextWrapped("Checks use the latest catalog and Ghostty poll results. The optional test launches the app you choose; no app is launched by refreshing checks.");
        if (doctor.Report is { } current)
            foreach (var step in current.Steps)
            {
                ImGui.Separator();
                ImGui.TextColored(step.Verdict == DoctorVerdict.Failed ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudWhite, step.Name + ": " + step.Verdict);
                ImGui.TextWrapped(step.Evidence);
                if (step.Fix.Length > 0) ImGui.TextWrapped(step.Fix);
            }
        ImGui.Separator();
        if (ImGui.BeginCombo("Optional test app", desktop.Catalog.Catalog.Find(selected)?.Name ?? "Choose a harmless graphical app"))
        {
            foreach (var app in desktop.Catalog.Catalog.Apps.Where(a => !a.Terminal))
                if (ImGui.Selectable(app.Name + "##doctor:" + app.Id, app.Id == selected)) selected = app.Id;
            ImGui.EndCombo();
        }
        if (!doctor.Probing && ImGui.Button("Launch selected app and verify its panel")) doctor.TestLaunch(selected);
        if (doctor.LastMessage is { } message) ImGui.TextWrapped(message);
    }
}
