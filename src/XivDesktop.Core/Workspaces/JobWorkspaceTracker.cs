namespace XivDesktop.Core.Workspaces;

/// <summary>Switch only on a real job change or a changed mapping, never on every polling tick.</summary>
public sealed class JobWorkspaceTracker
{
    private uint lastJob;
    private int lastWorkspace;

    public int? Update(uint job, IReadOnlyDictionary<uint, int> mapping)
    {
        var workspace = mapping.TryGetValue(job, out var n) && n is >= 1 and <= WorkspaceModel.Count ? n : 0;
        if (job == lastJob && workspace == lastWorkspace)
            return null;
        lastJob = job;
        lastWorkspace = workspace;
        return job != 0 && workspace != 0 ? workspace : null;
    }
}
