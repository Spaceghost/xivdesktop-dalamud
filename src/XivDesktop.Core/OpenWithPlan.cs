namespace XivDesktop.Core;

/// <summary>Build explicit Open With launches, retaining terminal handling and the parent app id.</summary>
public static class OpenWithPlan
{
    public static (IReadOnlyList<AppInfo> Apps, string? Error) Build(AppInfo app, IReadOnlyList<string> selectedPaths)
    {
        if (selectedPaths.Count is < 1 or > 32) return ([], "Select between one and 32 files.");
        if (string.IsNullOrWhiteSpace(app.Exec)) return ([], "This app's .desktop file is not visible to the game; its Exec is needed to open files.");
        var paths = selectedPaths.Select(p => p.StartsWith('/') ? p : HostPaths.LinuxFromWine(p)).ToList();
        if (paths.Any(p => p == null || p.Any(char.IsControl)))
            return ([], "Select files on the Linux host (Wine drive Z:); other Wine drives cannot be opened by a Linux app.");
        var groups = ExecLine.TakesMultipleFiles(app.Exec)
            ? new List<IReadOnlyList<string>> { paths.Select(p => p!).ToList() }
            : paths.Select(p => (IReadOnlyList<string>)new[] { p! }).ToList();
        var plans = new List<AppInfo>();
        foreach (var files in groups)
        {
            var command = ExecLine.OpenCommand(app.Exec, files);
            if (string.IsNullOrWhiteSpace(command)) return ([], "The app's Exec cannot safely expand the selected files.");
            plans.Add(app with { Command = command, AgentId = null });
        }
        return (plans, null);
    }
}
