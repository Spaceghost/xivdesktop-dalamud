using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Plugin.Services;
using XivDesktop.Core.Workspaces;
using XivDesktop.Core.Layouts;

namespace XivDesktop.Plugin.Services;

/// <summary>Reads the local player's job and duty conditions; it never changes the game state.</summary>
public sealed unsafe class DesktopContextService : IDisposable
{
    private readonly IFramework framework;
    private readonly IObjectTable objects;
    private readonly ICondition conditions;
    private readonly IGameGui gameGui;
    private readonly IClientState client;
    private readonly Configuration config;
    private readonly SessionService session;
    private readonly IPluginLog log;
    private readonly JobWorkspaceTracker jobs = new();
    private readonly ZoneLayoutTracker zones = new();
    private readonly StowLatch latch = new();

    public DesktopContextService(IFramework framework, IObjectTable objects, ICondition conditions,
        IClientState client, IGameGui gameGui, Configuration config, SessionService session, IPluginLog log)
    {
        this.framework = framework;
        this.objects = objects;
        this.conditions = conditions;
        this.client = client;
        this.gameGui = gameGui;
        this.config = config;
        this.session = session;
        this.log = log;
        framework.Update += Update;
        client.TerritoryChanged += TerritoryChanged;
    }

    private void Update(IFramework _)
    {
        try
        {
            var job = client.IsLoggedIn ? objects.LocalPlayer?.ClassJob.RowId ?? 0 : 0;
            if (jobs.Update(job, config.JobWorkspaces) is { } workspace)
                session.SwitchWorkspace(workspace);
            var confirmation = gameGui.GetAddonByName<AtkUnitBase>("ContentsFinderConfirm");
            var requested = config.StowInCombat && conditions[ConditionFlag.InCombat]
                || config.StowInDuty && (conditions[ConditionFlag.BoundByDuty]
                    || conditions[ConditionFlag.BoundByDuty56] || conditions[ConditionFlag.BoundByDuty95])
                || config.StowOnDutyPop && confirmation != null && confirmation->IsVisible;
            session.SetGpose(client.IsGPosing);
            session.SetStowed(latch.Update(requested, Environment.TickCount64));
            session.CurrentTerritory = client.IsLoggedIn ? client.TerritoryType : 0;
            var loading = conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51];
            var inDuty = conditions[ConditionFlag.BoundByDuty] || conditions[ConditionFlag.BoundByDuty56] || conditions[ConditionFlag.BoundByDuty95];
            if (zones.Update(session.CurrentTerritory, config.ZoneLayouts.GetValueOrDefault(session.CurrentTerritory),
                client.IsLoggedIn && objects.LocalPlayer != null && !loading, inDuty,
                session.ContextBusy || !session.Windows.WindowsAvailable || session.Windows.Snapshot.Windows.Any(w => w.State == "pending"), Environment.TickCount64) is { } rule)
            {
                if (rule.Workspace > 0) session.SwitchWorkspace(rule.Workspace);
                else session.ApplyPlate(rule.Plate);
            }
        }
        catch (Exception ex)
        {
            log.Debug(ex, "XivDesktop: context update failed");
        }
    }

    private void TerritoryChanged(uint _)
    {
        zones.Invalidate();
        session.TerritoryTransition();
    }

    public void Dispose()
    {
        framework.Update -= Update;
        client.TerritoryChanged -= TerritoryChanged;
    }
}
