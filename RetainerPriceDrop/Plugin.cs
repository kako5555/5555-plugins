using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Game.Network.Structures;
using Dalamud.Game.Addon.Lifecycle;
using ECommons;
using ECommons.DalamudServices;
using RetainerPriceDrop.Windows;
using RetainerPriceDrop.Services;

namespace RetainerPriceDrop;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Retainer Price Drop";
    private const string CommandName = "/pricedrop";

    private IDalamudPluginInterface PluginInterface { get; init; }
    private ICommandManager CommandManager { get; init; }
    private IMarketBoard MarketBoard { get; init; }
    
    public WindowSystem WindowSystem = new("RetainerPriceDrop");
    private MainWindow MainWindow { get; set; }
    private RetainerPriceController Controller { get; set; }
    private RetainerTracker Tracker { get; set; }
    private NewListingMonitor ListingMonitor { get; set; }
    public Configuration Configuration { get; private set; }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IMarketBoard marketBoard,
        IAddonLifecycle addonLifecycle)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        MarketBoard = marketBoard;

        ECommonsMain.Init(pluginInterface, this);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        
        Tracker = new RetainerTracker(pluginInterface);
        Controller = new RetainerPriceController(marketBoard, Tracker, Configuration);
        ListingMonitor = new NewListingMonitor(marketBoard, addonLifecycle, Configuration);
        MainWindow = new MainWindow(Controller, Tracker, Configuration);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens Retainer Price Drop window"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        
        // Hook to detect when RetainerList opens
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Controller?.Dispose();
        ListingMonitor?.Dispose();
        
        Svc.Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        
        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
        
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.IsOpen = true;
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }
    
    private void OpenMainUi()
    {
        MainWindow.IsOpen = true;
    }
    
    private void OpenConfigUi()
    {
        MainWindow.IsOpen = true;
    }
    
    private bool wasRetainerListOpen = false;
    
    private void OnFrameworkUpdate(IFramework framework)
    {
        bool shouldOpen = false;
        bool shouldClose = false;
        
        unsafe
        {
            // Check if RetainerList window is open
            if (ECommons.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RetainerList", out var retainerList) && 
                ECommons.GenericHelpers.IsAddonReady(retainerList))
            {
                if (!wasRetainerListOpen)
                {
                    // RetainerList just opened
                    wasRetainerListOpen = true;
                    shouldOpen = true;
                }
            }
            else
            {
                if (wasRetainerListOpen)
                {
                    // RetainerList just closed
                    shouldClose = true;
                }
                wasRetainerListOpen = false;
            }
        }
        
        if (shouldOpen)
        {
            MainWindow.IsOpen = true;
        }
        else if (shouldClose)
        {
            // Only close our window if automation is not running
            // (RetainerList closes when we select a retainer, but we want to keep the window open)
            if (!Controller.IsRunning)
            {
                MainWindow.IsOpen = false;
            }
        }
    }
}