using ECommons.DalamudServices;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using AddonRetainerSell = FFXIVClientStructs.FFXIV.Client.UI.AddonRetainerSell;
using AddonContextMenu = FFXIVClientStructs.FFXIV.Client.UI.AddonContextMenu;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AtkComponentList = FFXIVClientStructs.FFXIV.Component.GUI.AtkComponentList;
using static ECommons.GenericHelpers;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Linq;
using Dalamud.Plugin.Services;
using Dalamud.Game.Network.Structures;

namespace RetainerPriceDrop.Services;

public class RetainerPriceController : IDisposable
{
    private readonly IMarketBoard marketBoard;
    private readonly RetainerTracker tracker;
    private readonly Configuration config;
    private AutomationState currentState = AutomationState.Idle;
    private int currentItemIndex = 0;
    private DateTime lastActionTime = DateTime.Now;
    private readonly TimeSpan actionDelay = TimeSpan.FromMilliseconds(200);
    private uint lowestMarketPrice = 0;
    private int totalItemsProcessed = 0;
    private uint currentItemId = 0;
    private bool isPaused = false;
    private bool waitingForMarketData = false;
    private int lastRequestId = -1;
    private bool isSellingHqItem = false;
    private int targetRetainerIndex = -1;
    private string targetRetainerName = string.Empty;
    
    public bool IsRunning => currentState != AutomationState.Idle;
    public bool IsPaused => isPaused;
    public string Status => GetStatusText();
    public int ItemsProcessed => totalItemsProcessed;
    
    private enum AutomationState
    {
        Idle,
        SelectingRetainer,
        WaitingForDialogue,
        DismissingDialogue,
        WaitingForSelectString,
        SelectingSellItems,
        WaitingForRetainerSellList,
        GettingItemData,
        OpeningRetainerSell,
        WaitingForContextMenu,
        SelectingAdjustPrice,
        WaitingForRetainerSell,
        GettingMarketData,
        WaitingForMarketData,
        ClosingSearchResults,
        SettingPrice,
        ConfirmingPrice,
        NextItem,
        Finished,
        ClosingRetainerSellList,
        SelectingQuit,
        WaitingForRetainerDismissal,
        DismissingRetainerDialogue,
        WaitingForRetainerList
    }
    
    public RetainerPriceController(IMarketBoard marketBoard, RetainerTracker tracker, Configuration config)
    {
        this.marketBoard = marketBoard;
        this.tracker = tracker;
        this.config = config;
        marketBoard.OfferingsReceived += OnMarketBoardOfferingsReceived;
    }
    
    public void Start()
    {
        if (IsRunning)
        {
            Svc.Log.Warning("Already running");
            return;
        }
        
        unsafe
        {
            if (!TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) || !IsAddonReady(addon))
            {
                Svc.Log.Error("RetainerSellList not open");
                return;
            }
        }
        
        currentState = AutomationState.GettingItemData;
        currentItemIndex = 0;
        totalItemsProcessed = 0;
        Svc.Framework.Update += OnUpdate;
        Svc.Log.Info("Starting direct price drop automation");
    }
    
    public void StartForRetainer(int retainerIndex, string retainerName)
    {
        if (IsRunning)
        {
            Svc.Log.Warning("Already running");
            return;
        }
        
        targetRetainerIndex = retainerIndex;
        targetRetainerName = retainerName;
        currentItemIndex = 0;
        totalItemsProcessed = 0;
        
        // Check if RetainerList is already open
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerList", out var retainerList) && IsAddonReady(retainerList))
            {
                currentState = AutomationState.SelectingRetainer;
                Svc.Framework.Update += OnUpdate;
                Svc.Log.Info($"Starting automation for retainer: {retainerName} (index {retainerIndex})");
            }
            else
            {
                Svc.Log.Error("RetainerList not open. Please interact with a retainer bell first.");
            }
        }
    }
    
    public void Stop()
    {
        currentState = AutomationState.Idle;
        isPaused = false;
        Svc.Framework.Update -= OnUpdate;
        Svc.Log.Info("Stopped automation");
    }
    
    public void Pause()
    {
        isPaused = true;
        Svc.Log.Info("Paused automation");
    }
    
    public void Resume()
    {
        isPaused = false;
        Svc.Log.Info("Resumed automation");
    }
    
    public void Dispose()
    {
        marketBoard.OfferingsReceived -= OnMarketBoardOfferingsReceived;
        Stop();
    }
    
    private void OnUpdate(IFramework framework)
    {
        if (isPaused) return;
        
        if (DateTime.Now - lastActionTime < actionDelay) return;
        
        try
        {
            switch (currentState)
            {
                case AutomationState.SelectingRetainer:
                    SelectRetainer();
                    break;
                case AutomationState.WaitingForDialogue:
                    WaitForDialogue();
                    break;
                case AutomationState.DismissingDialogue:
                    DismissDialogue();
                    break;
                case AutomationState.WaitingForSelectString:
                    WaitForSelectString();
                    break;
                case AutomationState.SelectingSellItems:
                    SelectSellItems();
                    break;
                case AutomationState.WaitingForRetainerSellList:
                    WaitForRetainerSellList();
                    break;
                case AutomationState.GettingItemData:
                    GetItemData();
                    break;
                case AutomationState.OpeningRetainerSell:
                    OpenRetainerSell();
                    break;
                case AutomationState.WaitingForContextMenu:
                    WaitForContextMenu();
                    break;
                case AutomationState.SelectingAdjustPrice:
                    currentState = AutomationState.WaitingForRetainerSell;
                    break;
                case AutomationState.WaitingForRetainerSell:
                    WaitForRetainerSell();
                    break;
                case AutomationState.GettingMarketData:
                    GetMarketData();
                    break;
                case AutomationState.WaitingForMarketData:
                    WaitForMarketData();
                    break;
                case AutomationState.ClosingSearchResults:
                    CloseSearchResults();
                    break;
                case AutomationState.SettingPrice:
                    SetPrice();
                    break;
                case AutomationState.ConfirmingPrice:
                    ConfirmPrice();
                    break;
                case AutomationState.NextItem:
                    NextItem();
                    break;
                case AutomationState.Finished:
                    if (DateTime.Now - lastActionTime > actionDelay)
                    {
                        HandleFinished();
                    }
                    break;
                case AutomationState.ClosingRetainerSellList:
                    CloseRetainerSellList();
                    break;
                case AutomationState.SelectingQuit:
                    SelectQuit();
                    break;
                case AutomationState.WaitingForRetainerDismissal:
                    WaitForRetainerDismissal();
                    break;
                case AutomationState.DismissingRetainerDialogue:
                    DismissRetainerDialogue();
                    break;
                case AutomationState.WaitingForRetainerList:
                    WaitForRetainerList();
                    break;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Automation error: {ex}");
            Stop();
        }
    }
    
    private void SelectRetainer()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && IsAddonReady(addon))
            {
                Svc.Log.Info($"Attempting to select retainer: {targetRetainerName}");
                
                // Use AutoRetainer's approach with AddonMaster.RetainerList
                var retainerList = new AddonMaster.RetainerList(addon);
                
                // Find and select the retainer by name
                foreach (var retainer in retainerList.Retainers)
                {
                    if (retainer.Name == targetRetainerName)
                    {
                        Svc.Log.Info($"Found retainer {retainer.Name} at index {retainer.Index}, selecting...");
                        retainer.Select();
                        
                        currentState = AutomationState.WaitingForDialogue;
                        lastActionTime = DateTime.Now;
                        return;
                    }
                }
                
                // If we couldn't find by name, try by index as fallback
                Svc.Log.Warning($"Could not find retainer '{targetRetainerName}' by name, trying by index {targetRetainerIndex}");
                var retainerByIndex = retainerList.Retainers.ElementAtOrDefault(targetRetainerIndex);
                if (retainerByIndex != null)
                {
                    Svc.Log.Info($"Selecting retainer at index {targetRetainerIndex}: {retainerByIndex.Name}");
                    retainerByIndex.Select();
                }
                else
                {
                    Svc.Log.Error($"Could not find retainer at index {targetRetainerIndex}");
                }
                
                currentState = AutomationState.WaitingForDialogue;
                lastActionTime = DateTime.Now;
            }
        }
    }
    
    private void WaitForDialogue()
    {
        unsafe
        {
            // Check for Talk addon (dialogue window)
            if (TryGetAddonByName<AtkUnitBase>("Talk", out var talk) && IsAddonReady(talk))
            {
                Svc.Log.Info("Dialogue appeared, waiting to dismiss");
                currentState = AutomationState.DismissingDialogue;
                lastActionTime = DateTime.Now;
            }
            else if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(1.5))
            {
                // Sometimes dialogue doesn't appear, check if SelectString is already open (reduced timeout)
                if (TryGetAddonByName<AtkUnitBase>("SelectString", out var selectString) && IsAddonReady(selectString))
                {
                    Svc.Log.Info("SelectString already open, skipping dialogue");
                    currentState = AutomationState.SelectingSellItems;
                    lastActionTime = DateTime.Now;
                }
            }
        }
    }
    
    private void DismissDialogue()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("Talk", out var talk) && IsAddonReady(talk))
            {
                // Click through the dialogue
                ECommons.Automation.Callback.Fire(talk, true, 0);
                
                Svc.Log.Info("Dismissed dialogue");
                // Wait for SelectString to appear instead of jumping directly
                currentState = AutomationState.WaitingForSelectString;
                lastActionTime = DateTime.Now;
            }
            else
            {
                // Talk window is gone, move to waiting for SelectString
                Svc.Log.Info("Talk window no longer present, waiting for SelectString");
                currentState = AutomationState.WaitingForSelectString;
                lastActionTime = DateTime.Now;
            }
        }
    }
    
    private void WaitForSelectString()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && IsAddonReady(addon))
            {
                Svc.Log.Info("SelectString menu opened");
                currentState = AutomationState.SelectingSellItems;
                lastActionTime = DateTime.Now;
            }
            else if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(1.5))
            {
                // Try to go directly to selecting if we timeout (reduced from 3s to avoid AutoRetainer bailout)
                Svc.Log.Warning("SelectString wait timeout, attempting to select anyway");
                currentState = AutomationState.SelectingSellItems;
                lastActionTime = DateTime.Now;
            }
        }
    }
    
    private void SelectSellItems()
    {
        unsafe
        {
            if (TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonSelectString>("SelectString", out var addon) && IsAddonReady(&addon->AtkUnitBase))
            {
                // Small delay to ensure the menu is fully loaded (reduced for AutoRetainer compatibility)
                if (DateTime.Now - lastActionTime < TimeSpan.FromMilliseconds(250))
                {
                    return; // Wait a bit more
                }
                
                Svc.Log.Info("SelectString found, selecting sell items");
                
                // Use direct callback method for speed
                var eventArgs = stackalloc AtkValue[2];
                eventArgs[0] = new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = 2 }; // Select 3rd option (index 2)
                eventArgs[1] = new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = 0 };
                
                addon->AtkUnitBase.FireCallback(2, eventArgs);
                Svc.Log.Info("Selected 'Sell items in your inventory on the market' via direct callback");
                
                currentState = AutomationState.WaitingForRetainerSellList;
                lastActionTime = DateTime.Now;
            }
            else
            {
                // If SelectString isn't ready yet, retry quickly
                Svc.Log.Warning("SelectString not ready, will retry");
            }
        }
    }
    
    private void WaitForRetainerSellList()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && IsAddonReady(addon))
            {
                Svc.Log.Info("RetainerSellList opened successfully");
                currentState = AutomationState.GettingItemData;
                lastActionTime = DateTime.Now;
            }
            else if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(3))
            {
                Svc.Log.Error("RetainerSellList failed to open");
                Stop();
            }
        }
    }
    
    private void GetItemData()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && IsAddonReady(addon))
            {
                var listNode = addon->GetNodeById(11);
                if (listNode != null && listNode->GetAsAtkComponentNode() != null && listNode->GetAsAtkComponentNode()->Component != null)
                {
                    var list = (AtkComponentList*)listNode->GetAsAtkComponentNode()->Component;
                    if (currentItemIndex < list->ListLength)
                    {
                        var itemRenderer = list->ItemRendererList[currentItemIndex].AtkComponentListItemRenderer;
                        if (itemRenderer != null)
                        {
                            currentItemId = (uint)currentItemIndex;
                            
                            Svc.Log.Info($"Processing item {currentItemIndex}, ID: {currentItemId}");
                            currentState = AutomationState.OpeningRetainerSell;
                            lastActionTime = DateTime.Now;
                        }
                    }
                    else
                    {
                        Svc.Log.Info($"No items to process. Total processed: {totalItemsProcessed}");
                        currentState = AutomationState.Finished;
                        lastActionTime = DateTime.Now;
                    }
                }
            }
        }
    }
    
    
    private void OpenRetainerSell()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && IsAddonReady(addon))
            {
                Svc.Log.Info($"Opening RetainerSell directly for item {currentItemIndex} using Agent system");
                var retainerAgent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
                if (retainerAgent == null)
                {
                    Svc.Log.Error("Retainer agent is null");
                    currentState = AutomationState.NextItem;
                    lastActionTime = DateTime.Now;
                    return;
                }
                
                // Check if we have items to process
                var retainerManager = RetainerManager.Instance();
                if (retainerManager == null || retainerManager->GetActiveRetainer() == null)
                {
                    Svc.Log.Error("No active retainer");
                    currentState = AutomationState.Finished;
                    return;
                }
                
                var marketItemCount = retainerManager->GetActiveRetainer()->MarketItemCount;
                if (currentItemIndex >= marketItemCount)
                {
                    Svc.Log.Info($"Completed all {marketItemCount} items");
                    currentState = AutomationState.Finished;
                    return;
                }
                
                var eventArgs = (AtkValue*)Marshal.AllocHGlobal(3 * sizeof(AtkValue));
                var returnValue = (AtkValue*)Marshal.AllocHGlobal(sizeof(AtkValue));
                
                try
                {
                    eventArgs[0] = new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = 0 };
                    eventArgs[1] = new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = currentItemIndex };
                    eventArgs[2] = new() { Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int, Int = 1 };
                    
                    retainerAgent->ReceiveEvent(returnValue, eventArgs, 3, 3);
                    Svc.Log.Info($"Sent ReceiveEvent(3, [0, {currentItemIndex}, 1]) to open context menu");
                    currentState = AutomationState.WaitingForContextMenu;
                    lastActionTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    Svc.Log.Error($"Error sending event: {ex}");
                    currentState = AutomationState.NextItem;
                    lastActionTime = DateTime.Now;
                }
                finally
                {
                    Marshal.FreeHGlobal(new IntPtr(eventArgs));
                    Marshal.FreeHGlobal(new IntPtr(returnValue));
                }
            }
        }
    }
    
    private void WaitForContextMenu()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("ContextMenu", out var contextMenuBase) && IsAddonReady(contextMenuBase))
            {
                Svc.Log.Info("Context menu opened successfully");
                
                // Wait a brief moment for the menu to fully populate
                System.Threading.Thread.Sleep(100);
                
                // Method 1: Try using the standard callback for context menus
                // The callback typically uses params: true, eventType, index, unknown params...
                // For context menus, event type 0 usually means "select item"
                // Index 0 should be "Adjust Price" as the first option
                ECommons.Automation.Callback.Fire(contextMenuBase, true, 0, 0, 0, 0);
                
                // Give it a moment to process
                System.Threading.Thread.Sleep(100);
                
                // Check if RetainerSell opened (which means it worked)
                if (TryGetAddonByName<AtkUnitBase>("RetainerSell", out var retainerSell))
                {
                    Svc.Log.Info("Successfully fired callback to select 'Adjust Price'");
                }
                else
                {
                    Svc.Log.Warning("Callback returned false, trying alternative method");
                    
                    // Method 2: Try the OnMenuSelected method
                    var contextMenu = (AddonContextMenu*)contextMenuBase;
                    var selectResult = contextMenu->OnMenuSelected(0, 0);
                    
                    if (selectResult)
                    {
                        Svc.Log.Info("Successfully used OnMenuSelected for 'Adjust Price'");
                    }
                    else
                    {
                        // Method 3: Try firing with different parameters based on your node info
                        // Using param 1 as you mentioned from the events
                        ECommons.Automation.Callback.Fire(contextMenuBase, true, 1, 0, 0, 0);
                        Svc.Log.Info("Tried alternative callback with param 1");
                    }
                }
                
                currentState = AutomationState.WaitingForRetainerSell;
                lastActionTime = DateTime.Now;
            }
            else
            {
                if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(2))
                {
                    Svc.Log.Warning("Context menu failed to open, retrying");
                    currentState = AutomationState.OpeningRetainerSell;
                    lastActionTime = DateTime.Now;
                }
            }
        }
    }
    
    
    private void WaitForRetainerSell()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerSell", out var addon) && IsAddonReady(addon))
            {
                // Check if the item being sold is HQ by looking for the HQ icon in the item name
                var retainerSell = (AddonRetainerSell*)addon;
                if (retainerSell->ItemName != null)
                {
                    var itemNameText = retainerSell->ItemName->NodeText.ToString();
                    isSellingHqItem = itemNameText.Contains('\uE03C'); // HQ icon unicode character
                    Svc.Log.Info($"RetainerSell opened - Selling {(isSellingHqItem ? "HQ" : "Normal")} item: {itemNameText}");
                }
                else
                {
                    isSellingHqItem = false;
                    Svc.Log.Warning("Could not read item name from RetainerSell");
                }
                
                currentState = AutomationState.GettingMarketData;
                lastActionTime = DateTime.Now;
            }
            else
            {
                if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(3))
                {
                    Svc.Log.Error("RetainerSell failed to open - retrying");
                    currentState = AutomationState.OpeningRetainerSell;
                    lastActionTime = DateTime.Now;
                }
            }
        }
    }
    
    private void GetMarketData()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerSell", out var addon) && IsAddonReady(addon))
            {
                waitingForMarketData = true;
                lowestMarketPrice = 0;
                
                ECommons.Automation.Callback.Fire(addon, true, 4);
                
                currentState = AutomationState.WaitingForMarketData;
                lastActionTime = DateTime.Now;
                Svc.Log.Info("Clicked Compare Prices button, waiting for market data");
            }
        }
    }
    
    private void WaitForMarketData()
    {
        unsafe
        {
            // Check if ItemSearchResult window is open
            if (TryGetAddonByName<AtkUnitBase>("ItemSearchResult", out var addon) && IsAddonReady(addon))
            {
                // Check if we received market data
                if (!waitingForMarketData && lowestMarketPrice > 0)
                {
                    Svc.Log.Info($"Market data received: {lowestMarketPrice} gil");
                    currentState = AutomationState.ClosingSearchResults;
                    lastActionTime = DateTime.Now;
                }
                else if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(3))
                {
                    Svc.Log.Warning("Timeout waiting for market data - will skip price update");
                    lowestMarketPrice = 0; // Use 0 to indicate no market data
                    waitingForMarketData = false;
                    currentState = AutomationState.ClosingSearchResults;
                    lastActionTime = DateTime.Now;
                }
            }
            else if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(3))
            {
                Svc.Log.Error("ItemSearchResult failed to open - will skip price update");
                lowestMarketPrice = 0; // Use 0 to indicate no market data
                waitingForMarketData = false;
                currentState = AutomationState.ClosingSearchResults;
                lastActionTime = DateTime.Now;
            }
        }
    }
    
    private void OnMarketBoardOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        if (!waitingForMarketData) return;
        
        // Prevent duplicate processing
        if (offerings.RequestId == lastRequestId) return;
        lastRequestId = offerings.RequestId;
        
        Svc.Log.Info($"Market board data received for {offerings.ItemListings.Count} listings");
        
        if (offerings.ItemListings.Count > 0)
        {
            // Filter listings based on HQ status if we're selling an HQ item
            IMarketBoardItemListing? lowestListing = null;
            
            if (isSellingHqItem)
            {
                // Find the lowest HQ listing
                Svc.Log.Info("Filtering for HQ listings only");
                foreach (var listing in offerings.ItemListings)
                {
                    if (listing.IsHq)
                    {
                        lowestListing = listing;
                        break; // First HQ listing is the cheapest HQ
                    }
                }
                
                if (lowestListing == null)
                {
                    Svc.Log.Warning("No HQ listings found, using lowest normal quality price");
                    lowestListing = offerings.ItemListings[0];
                }
            }
            else
            {
                // For normal quality, just use the first (cheapest) listing
                lowestListing = offerings.ItemListings[0];
            }
            
            lowestMarketPrice = lowestListing.PricePerUnit;
            Svc.Log.Info($"Lowest {(lowestListing.IsHq ? "HQ" : "NQ")} market price found: {lowestMarketPrice} gil (from retainer: {lowestListing.RetainerName})");
            
            // Log a few prices for debugging
            for (int i = 0; i < Math.Min(5, offerings.ItemListings.Count); i++)
            {
                var listing = offerings.ItemListings[i];
                Svc.Log.Debug($"  Listing {i + 1}: {listing.PricePerUnit} gil - {(listing.IsHq ? "HQ" : "NQ")} - {listing.RetainerName}");
            }
        }
        else
        {
            Svc.Log.Warning("No market listings found, will skip price update");
            lowestMarketPrice = 0; // Use 0 to indicate no market data
        }
        
        waitingForMarketData = false;
    }
    
    private void CloseSearchResults()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("ItemSearchResult", out var addon) && IsAddonReady(addon))
            {
                ECommons.Automation.Callback.Fire(addon, true, -1);
                
                currentState = AutomationState.SettingPrice;
                lastActionTime = DateTime.Now;
                Svc.Log.Info("Closed ItemSearchResult with ESC key");
            }
            else
            {
                currentState = AutomationState.SettingPrice;
                lastActionTime = DateTime.Now;
            }
        }
    }
    
    private void SetPrice()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerSell", out var addon) && IsAddonReady(addon))
            {
                // Check if we have market data
                if (lowestMarketPrice == 0)
                {
                    // No market data - skip price change and just confirm
                    Svc.Log.Info("No market data available - keeping existing price");
                    currentState = AutomationState.ConfirmingPrice;
                    lastActionTime = DateTime.Now;
                    return;
                }
                
                // Calculate the undercut price using configuration
                uint undercutAmount = (uint)config.UndercutAmount;
                uint newPrice;
                if (lowestMarketPrice > undercutAmount)
                {
                    newPrice = lowestMarketPrice - undercutAmount;  // Undercut by configured amount
                }
                else
                {
                    newPrice = 1;  // Minimum price
                }
                
                Svc.Log.Info($"Calculating price: Market={lowestMarketPrice}, Undercut={undercutAmount}, NewPrice={newPrice}");
                
                // Get the NumericInput component (node 10)
                var numericInputNode = addon->GetNodeById(10);
                if (numericInputNode != null && numericInputNode->GetAsAtkComponentNode() != null)
                {
                    var numericInputComponent = (AtkComponentNumericInput*)numericInputNode->GetAsAtkComponentNode()->Component;
                    
                    // Log current value
                    var currentValue = numericInputComponent->AtkComponentInputBase.AtkTextNode->NodeText.ToString();
                    Svc.Log.Debug($"Current field value: {currentValue}");
                    
                    // Set the value directly on the NumericInput component
                    numericInputComponent->SetValue((int)newPrice);
                    Svc.Log.Debug($"Called SetValue with: {newPrice}");
                    
                    // Also try to update via the input base
                    var inputBase = &numericInputComponent->AtkComponentInputBase;
                    if (inputBase->AtkTextNode != null)
                    {
                        inputBase->AtkTextNode->SetText(newPrice.ToString());
                        Svc.Log.Debug($"Set text directly to: {newPrice}");
                    }
                    
                    // Fire the callback to notify the game of the value change
                    // Callback params: true, eventType=3 (value change), value, unknown
                    ECommons.Automation.Callback.Fire(addon, true, 3, newPrice, 0);
                    Svc.Log.Debug($"Fired callback with value: {newPrice}");
                    
                    // Verify the value was set
                    System.Threading.Thread.Sleep(100);
                    var newValue = numericInputComponent->AtkComponentInputBase.AtkTextNode->NodeText.ToString();
                    Svc.Log.Info($"Price field after setting: {newValue}");
                }
                else
                {
                    Svc.Log.Warning("Could not find NumericInput component at node 10");
                }
                
                currentState = AutomationState.ConfirmingPrice;
                lastActionTime = DateTime.Now;
            }
        }
    }
    
    private void ConfirmPrice()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerSell", out var addon) && IsAddonReady(addon))
            {
                ECommons.Automation.Callback.Fire(addon, true, 0);
                
                currentState = AutomationState.NextItem;
                lastActionTime = DateTime.Now;
                totalItemsProcessed++;
                Svc.Log.Info($"Confirmed price change for item {currentItemIndex}");
            }
        }
    }
    
    private void NextItem()
    {
        currentItemIndex++;
        
        if (currentItemIndex >= 20)
        {
            Svc.Log.Info("Reached maximum items (20), finishing...");
            currentState = AutomationState.Finished;
            lastActionTime = DateTime.Now;
        }
        else
        {
            currentState = AutomationState.GettingItemData;
            lastActionTime = DateTime.Now;
        }
    }
    
    private void HandleFinished()
    {
        Svc.Log.Info($"Completed all items. Processed: {totalItemsProcessed}");
        
        // Record the price drop timestamp for this retainer
        if (!string.IsNullOrEmpty(targetRetainerName) && totalItemsProcessed > 0)
        {
            tracker.RecordPriceDrop(targetRetainerName);
            Svc.Log.Info($"Recorded price drop timestamp for {targetRetainerName}");
        }
        
        // If we started from retainer selection, close the retainer and return to list
        if (!string.IsNullOrEmpty(targetRetainerName))
        {
            Svc.Log.Info($"Starting dismissal sequence for retainer {targetRetainerName}");
            currentState = AutomationState.ClosingRetainerSellList;
            lastActionTime = DateTime.Now;
        }
        else
        {
            // Just started from RetainerSellList directly, stop here
            Svc.Log.Info("No target retainer, stopping here");
            Stop();
        }
    }
    
    private void CloseRetainerSellList()
    {
        unsafe
        {
            if (TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && IsAddonReady(addon))
            {
                addon->Close(true);
                Svc.Log.Info("Closed RetainerSellList");
                currentState = AutomationState.SelectingQuit;
                lastActionTime = DateTime.Now;
            }
            else
            {
                // Already closed, check if SelectString is open
                if (TryGetAddonByName<AtkUnitBase>("SelectString", out var selectString) && IsAddonReady(selectString))
                {
                    Svc.Log.Info("RetainerSellList already closed, SelectString is open");
                    currentState = AutomationState.SelectingQuit;
                    lastActionTime = DateTime.Now;
                }
                else if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(2))
                {
                    Svc.Log.Warning("Timeout waiting for SelectString after closing RetainerSellList");
                    currentState = AutomationState.SelectingQuit;
                    lastActionTime = DateTime.Now;
                }
            }
        }
    }
    
    private void SelectQuit()
    {
        unsafe
        {
            if (TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonSelectString>("SelectString", out var addon) && IsAddonReady(&addon->AtkUnitBase))
            {
                var selectString = new AddonMaster.SelectString((nint)addon);
                
                // Find and select "Quit" option (usually the last one)
                var entriesList = selectString.Entries.ToList();
                if (entriesList.Count > 0)
                {
                    var quitEntry = entriesList.Last();
                    Svc.Log.Info($"Selecting Quit option: '{quitEntry.Text}'");
                    quitEntry.Select();
                }
                
                // Wait for the dialogue that appears after selecting Quit
                currentState = AutomationState.WaitingForRetainerDismissal;
                lastActionTime = DateTime.Now;
            }
            else if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(1.5))
            {
                Svc.Log.Warning("SelectString not found, might already be dismissed");
                currentState = AutomationState.WaitingForRetainerList;
                lastActionTime = DateTime.Now;
            }
        }
    }
    
    private void WaitForRetainerDismissal()
    {
        unsafe
        {
            // Check for Talk dialogue and click through it immediately
            if (TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonTalk>("Talk", out var talk) && talk->AtkUnitBase.IsVisible)
            {
                Svc.Log.Info("Found Talk dialogue after Quit, clicking through it");
                // Use callback to click through
                ECommons.Automation.Callback.Fire(&talk->AtkUnitBase, true, 0);
                // Move to waiting for RetainerList
                currentState = AutomationState.WaitingForRetainerList;
                lastActionTime = DateTime.Now;
                return;
            }
            
            // If no Talk dialogue after a moment, just wait for RetainerList
            if (DateTime.Now - lastActionTime > TimeSpan.FromMilliseconds(500))
            {
                currentState = AutomationState.WaitingForRetainerList;
                lastActionTime = DateTime.Now;
            }
        }
    }
    
    private void DismissRetainerDialogue()
    {
        unsafe
        {
            // This state is now deprecated - we handle everything in WaitForRetainerDismissal
            // Just transition to the next state
            currentState = AutomationState.WaitingForRetainerDismissal;
            lastActionTime = DateTime.Now;
        }
    }
    
    private void WaitForRetainerList()
    {
        unsafe
        {
            // Check if RetainerList is back
            if (TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && IsAddonReady(addon))
            {
                Svc.Log.Info("Successfully returned to RetainerList");
                Stop();
            }
            else if (DateTime.Now - lastActionTime > TimeSpan.FromSeconds(10))
            {
                // Give it more time since we're skipping dialogue handling
                Svc.Log.Warning("Timeout waiting for RetainerList - stopping anyway");
                Stop();
            }
        }
    }
    
    private string GetStatusText()
    {
        return currentState switch
        {
            AutomationState.Idle => "Ready",
            AutomationState.SelectingRetainer => $"Selecting {targetRetainerName}",
            AutomationState.WaitingForDialogue => "Waiting for dialogue",
            AutomationState.DismissingDialogue => "Dismissing dialogue",
            AutomationState.WaitingForSelectString => "Waiting for menu",
            AutomationState.SelectingSellItems => "Selecting market option",
            AutomationState.WaitingForRetainerSellList => "Loading market inventory",
            AutomationState.GettingItemData => $"Processing item {currentItemIndex + 1}",
            AutomationState.OpeningRetainerSell => "Opening context menu",
            AutomationState.WaitingForContextMenu => "Waiting for context menu",
            AutomationState.SelectingAdjustPrice => "Selecting adjust price",
            AutomationState.WaitingForRetainerSell => "Loading price editor",
            AutomationState.GettingMarketData => "Fetching market data",
            AutomationState.WaitingForMarketData => "Waiting for market data",
            AutomationState.ClosingSearchResults => "Closing search window",
            AutomationState.SettingPrice => lowestMarketPrice == 0 ? "No market data - keeping price" : $"Setting price to {(lowestMarketPrice > config.UndercutAmount ? lowestMarketPrice - (uint)config.UndercutAmount : 1)}",
            AutomationState.ConfirmingPrice => "Saving changes",
            AutomationState.NextItem => "Moving to next item",
            AutomationState.Finished => $"Complete - {totalItemsProcessed} items updated",
            AutomationState.ClosingRetainerSellList => "Closing market inventory",
            AutomationState.SelectingQuit => "Selecting quit option",
            AutomationState.WaitingForRetainerDismissal => "Waiting for retainer dismissal",
            AutomationState.DismissingRetainerDialogue => "Dismissing retainer",
            AutomationState.WaitingForRetainerList => "Returning to retainer list",
            _ => currentState.ToString()
        };
    }
}