using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using XivDesktop.Core;
using XivDesktop.Core.Workspaces;
using XivDesktop.Plugin.Services;

namespace XivDesktop.Plugin.Windows;

public sealed class ControllerRingWindow : Window, IDisposable
{
    private readonly IFramework framework;
    private readonly IGamepadState pad;
    private readonly IClientState client;
    private readonly ITextureProvider textures;
    private readonly Configuration config;
    private readonly DesktopService desktop;
    private readonly SessionService session;
    private readonly IPluginLog log;
    private readonly ControllerRing ring = new();
    private string? lastChord;
    private GamepadButtons[] chord = [];

    public ControllerRingWindow(IFramework framework, IGamepadState pad, IClientState client, ITextureProvider textures,
        Configuration config, DesktopService desktop, SessionService session, IPluginLog log)
        : base("App ring###XivDesktopControllerRing", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings)
    {
        this.framework = framework; this.pad = pad; this.client = client; this.textures = textures;
        this.config = config; this.desktop = desktop; this.session = session; this.log = log;
        framework.Update += Update;
    }

    public static GamepadButtons[] ParseChord(string? text)
    {
        var names = (text ?? "").Split('+', StringSplitOptions.TrimEntries);
        var parsed = new List<GamepadButtons>();
        foreach (var name in names)
        {
            if (!Enum.TryParse<GamepadButtons>(name, true, out var button) || !Enum.IsDefined(button)
                || !string.Equals(name, button.ToString(), StringComparison.OrdinalIgnoreCase)
                || button is GamepadButtons.None or GamepadButtons.L1 or GamepadButtons.R1 or GamepadButtons.East) return [];
            parsed.Add(button);
        }
        return parsed.Distinct().ToArray();
    }

    private void Update(IFramework _)
    {
        try
        {
            if (lastChord != config.ControllerRingChord)
            {
                lastChord = config.ControllerRingChord;
                chord = ParseChord(lastChord);
                ring.Update(true, Vector2.Zero, false, false, true, false, []);
            }
            var held = chord.Length > 0 && chord.All(b => pad.Raw(b) != 0);
            var allowed = config.ControllerRingEnabled && client.IsLoggedIn && KeybindService.GameInForeground()
                && !session.Stowed && !session.Veiled && !pad.EnableGamepadNav;
            var stick = pad.LeftStick;
            stick = stick.Length() > Math.Clamp(config.ControllerRingDeadzone, 1, 32767) ? Vector2.Normalize(stick) : Vector2.Zero;
            var favourites = desktop.Favourites.Select(id => desktop.Catalog.Catalog.Find(id)).OfType<AppInfo>();
            var result = ring.Update(held, stick, pad.Raw(GamepadButtons.L1) != 0, pad.Raw(GamepadButtons.R1) != 0,
                pad.Raw(GamepadButtons.East) != 0, allowed, favourites);
            IsOpen = ring.Open;
            if (result.WorkspaceStep != 0)
                session.SwitchWorkspace((session.CurrentWorkspace - 1 + result.WorkspaceStep + WorkspaceModel.Count) % WorkspaceModel.Count + 1);
            if (result.LaunchId is { } id) desktop.Launch(id);
        }
        catch (Exception ex) { ring.Close(); IsOpen = false; log.Debug(ex, "XivDesktop: controller ring failed"); }
    }

    public override void PreDraw()
    {
        Size = new Vector2(420, 420);
        SizeCondition = ImGuiCond.Always;
        Position = ImGui.GetMainViewport().GetCenter() - new Vector2(210);
        PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        if (session.Stowed || session.Veiled || !KeybindService.GameInForeground()) return;
        var draw = ImGui.GetWindowDrawList();
        var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2;
        draw.AddCircle(center, 130, 0x80808080, 64, 2);
        for (var i = 0; i < ring.Apps.Count; i++)
        {
            var angle = MathF.Tau * i / ring.Apps.Count;
            var pos = center + new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * 130 - new Vector2(24);
            if (i == ring.Selected) draw.AddCircle(pos + new Vector2(24), 33, 0xFF80D0FF, 32, 3);
            IconDrawer.App(textures, draw, ring.Apps[i], pos, 48, false);
        }
        var text = ring.Selected >= 0 ? ring.Apps[ring.Selected].Name : "Flick to select";
        draw.AddText(center - ImGui.CalcTextSize(text) / 2, 0xFFFFFFFF, text);
        draw.AddText(center + new Vector2(-70, 38), 0xFFFFFFFF, $"Workspace {session.CurrentWorkspace}");
    }

    public void Dispose() { framework.Update -= Update; ring.Close(); IsOpen = false; }
}
