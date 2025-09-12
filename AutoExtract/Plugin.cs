using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using AutoExtract.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using ECommons;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.Automation;
using ECommons.Throttlers;
using static ECommons.GenericHelpers;

namespace AutoExtract;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private const string CommandName = "/autoextract";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("AutoExtract");
    private ConfigWindow ConfigWindow { get; init; }

    private bool _isExtractWindowOpen = false;
    private int _currentCategory = 0;
    private int _stoppingCategory = 6;
    private bool _switchedCategory = false;

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);
        
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle AutoExtract configuration window"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Materialize", OnMaterializeOpen);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Materialize", OnMaterializeClose);
        PluginInterface.UiBuilder.Draw += DrawOverlay;

        Framework.Update += OnFrameworkUpdate;

        Log.Information("AutoExtract plugin loaded successfully");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= DrawOverlay;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;

        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Materialize", OnMaterializeOpen);
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "Materialize", OnMaterializeClose);

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        ConfigWindow.Toggle();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();

    private void OnMaterializeOpen(AddonEvent type, AddonArgs args)
    {
        _isExtractWindowOpen = true;
        _currentCategory = 0;
        _switchedCategory = false;
    }

    private void OnMaterializeClose(AddonEvent type, AddonArgs args)
    {
        _isExtractWindowOpen = false;
        _currentCategory = 0;
        _switchedCategory = false;
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        if (!Configuration.AutoExtractEnabled || !_isExtractWindowOpen)
            return;

        if (!EzThrottler.Throttle("AutoExtract", 250))
            return;

        if (Condition[ConditionFlag.Mounted])
        {
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23); // Dismount
            return;
        }

        // Check if player is occupied (similar to AutoDuty's PlayerHelper.IsOccupied)
        if (Condition[ConditionFlag.Occupied] || Condition[ConditionFlag.Occupied30] || 
            Condition[ConditionFlag.Occupied33] || Condition[ConditionFlag.Occupied38] ||
            Condition[ConditionFlag.Occupied39] || Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            Condition[ConditionFlag.OccupiedInQuestEvent] || Condition[ConditionFlag.OccupiedInEvent] ||
            Condition[ConditionFlag.OccupiedSummoningBell] || Condition[ConditionFlag.Casting] ||
            Condition[ConditionFlag.BetweenAreas])
            return;

        if (!TryGetAddonByName("Materialize", out AtkUnitBase* addonMaterialize))
        {
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 14);
            return;
        }

        if (!IsAddonReady(addonMaterialize))
            return;

        // Check for MaterializeDialog first
        if (TryGetAddonByName("MaterializeDialog", out AtkUnitBase* addonMaterializeDialog) && IsAddonReady(addonMaterializeDialog))
        {
            Log.Debug("AutoExtract - Confirming MaterializeDialog");
            new AddonMaster.MaterializeDialog(addonMaterializeDialog).Materialize();
            return;
        }

        if (_currentCategory <= _stoppingCategory)
        {
            var list = addonMaterialize->GetNodeById(12)->GetAsAtkComponentList();
            if (list == null) return;

            var spiritbondTextNode = list->UldManager.NodeList[2]->GetComponent()->GetTextNodeById(5)->GetAsAtkTextNode();
            if (spiritbondTextNode == null) return;

            // Switch to Category, if not on it
            if (!_switchedCategory)
            {
                Log.Debug($"AutoExtract - Switching to Category: {_currentCategory}");
                FireCallback(addonMaterialize, 1, _currentCategory);
                _switchedCategory = true;
                return;
            }

            string spiritbondText = spiritbondTextNode->NodeText.ToString().Replace(" ", string.Empty);
            if (spiritbondText == "100%")
            {
                Log.Debug("AutoExtract - Extracting Materia");
                FireCallback(addonMaterialize, 2, 0);
                return;
            }
            else
            {
                _currentCategory++;
                _switchedCategory = false;
            }
        }
        else
        {
            Log.Info("AutoExtract - Extract Materia Finished");
            addonMaterialize->Close(true);
            _isExtractWindowOpen = false;
            _currentCategory = 0;
            _switchedCategory = false;
        }
    }

    private unsafe void DrawOverlay()
    {
        if (!_isExtractWindowOpen) return;

        if (TryGetAddonByName("Materialize", out AtkUnitBase* addon))
        {
            var addonPos = new Vector2(addon->X, addon->Y);
            var addonSize = new Vector2(addon->GetScaledWidth(true), addon->GetScaledHeight(true));

            ImGui.SetNextWindowPos(new Vector2(addonPos.X + 10, addonPos.Y - 40));
            ImGui.SetNextWindowSize(new Vector2(180, 40));
            ImGui.SetNextWindowBgAlpha(0.8f);

            if (ImGui.Begin("AutoExtract Overlay", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar))
            {
                // Add equal padding left and right
                ImGui.SetCursorPosX(10);
                bool enabled = Configuration.AutoExtractEnabled;
                if (ImGui.Checkbox("Auto extract materia", ref enabled))
                {
                    Configuration.AutoExtractEnabled = enabled;
                    Configuration.Save();
                }
            }
            ImGui.End();
        }
    }

    private static unsafe bool IsAddonReady(AtkUnitBase* addon)
    {
        return addon != null && addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded;
    }

    private static unsafe void FireCallback(AtkUnitBase* addon, int eventType, int eventParam = 0)
    {
        if (addon == null) return;
        
        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = eventType };
        values[1] = new AtkValue { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = eventParam };
        
        addon->FireCallback(2, values);
    }
}