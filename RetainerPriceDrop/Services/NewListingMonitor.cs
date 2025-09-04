using System;
using System.Linq;
using Dalamud.Plugin.Services;
using Dalamud.Game.Network.Structures;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.Game;
using static ECommons.GenericHelpers;
using Dalamud.Bindings.ImGui;

namespace RetainerPriceDrop.Services;

public class NewListingMonitor : IDisposable
{
    private readonly IMarketBoard marketBoard;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly Configuration config;
    private DateTime lastCopyTime = DateTime.MinValue;
    private bool itemIsHq = false;
    private bool retainerSellOpen = false;
    private uint currentItemId = 0;
    private string currentItemName = string.Empty;
    private DateTime lastItemProcessedTime = DateTime.MinValue;
    private string lastProcessedItemName = string.Empty;
    
    public NewListingMonitor(IMarketBoard marketBoard, IAddonLifecycle addonLifecycle, Configuration config)
    {
        this.marketBoard = marketBoard;
        this.addonLifecycle = addonLifecycle;
        this.config = config;
        
        // Subscribe to market board data
        marketBoard.OfferingsReceived += OnMarketBoardOfferingsReceived;
        
        // Hook to monitor when RetainerSell window opens
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", OnRetainerSellPostSetup);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerSell", OnRetainerSellPreFinalize);
    }
    
    public void Dispose()
    {
        marketBoard.OfferingsReceived -= OnMarketBoardOfferingsReceived;
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSell", OnRetainerSellPostSetup);
        addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "RetainerSell", OnRetainerSellPreFinalize);
    }
    
    private unsafe void OnRetainerSellPostSetup(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonRetainerSell*)args.Addon.Address;
        if (addon == null) return;
        
        retainerSellOpen = true;
        
        // Check if item name contains HQ icon
        if (addon->ItemName != null)
        {
            var itemNameText = addon->ItemName->NodeText.ToString();
            currentItemName = itemNameText;
            itemIsHq = itemNameText.Contains('\uE03C'); // HQ icon unicode character
            
            // Reset the processed flag when a new item is opened
            if (lastProcessedItemName != currentItemName)
            {
                lastItemProcessedTime = DateTime.MinValue;
                lastProcessedItemName = string.Empty;
            }
            
            if (config.DebugMode)
                Svc.Chat.Print($"[PriceDrop Debug] RetainerSell opened - Item: {itemNameText}, IsHQ: {itemIsHq}");
        }
    }
    
    private void OnRetainerSellPreFinalize(AddonEvent type, AddonArgs args)
    {
        retainerSellOpen = false;
        currentItemId = 0;
        currentItemName = string.Empty;
        lastProcessedItemName = string.Empty;
        lastItemProcessedTime = DateTime.MinValue;
        if (config.DebugMode)
            Svc.Chat.Print($"[PriceDrop Debug] RetainerSell closed");
    }
    
    private void OnMarketBoardOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        // Only process if RetainerSell window is open
        if (!retainerSellOpen) 
        {
            return; // Silent ignore when window not open
        }
        
        // Check if this is data for the current item
        if (currentItemId != 0 && offerings.ItemListings.Count > 0)
        {
            // Verify this is the right item by checking if any listing matches our item ID
            // Unfortunately we can't directly check item ID from offerings, so we'll use timing
        }
            
        // Check if we've already processed this item recently
        if (currentItemName == lastProcessedItemName && DateTime.Now - lastItemProcessedTime < TimeSpan.FromSeconds(3.0))
        {
            if (config.DebugMode)
                Svc.Chat.Print($"[PriceDrop Debug] Already processed {currentItemName}, ignoring duplicate market data");
            return;
        }
        
        // Prevent copying the same price multiple times in quick succession
        if (DateTime.Now - lastCopyTime < TimeSpan.FromSeconds(1.0))
        {
            return; // Silent skip
        }
        
        if (config.DebugMode)
            Svc.Chat.Print($"[PriceDrop Debug] Processing market data for {currentItemName} - Listings: {offerings.ItemListings.Count}, Selling HQ: {itemIsHq}");
            
        // Find the lowest price that isn't ours
        var lowestPrice = uint.MaxValue;
        var hasListings = false;
        var skippedOwnCount = 0;
        var skippedNqCount = 0;
        var validListingCount = 0;
        
        // Debug: Show first 5 listings
        if (config.DebugMode)
        {
            var debugCount = 0;
            foreach (var listing in offerings.ItemListings.Take(5))
            {
                Svc.Chat.Print($"[PriceDrop Debug] Listing #{debugCount++}: {listing.RetainerName}, Price: {listing.PricePerUnit} gil, HQ: {listing.IsHq}");
            }
        }
        
        foreach (var listing in offerings.ItemListings)
        {
            // Skip our own retainer listings
            if (listing.RetainerName != null && IsOurRetainer(listing.RetainerName))
            {
                skippedOwnCount++;
                if (config.DebugMode)
                    Svc.Chat.Print($"[PriceDrop Debug] Skipped own retainer: {listing.RetainerName} at {listing.PricePerUnit} gil");
                continue;
            }
            
            // Skip NQ listings if we're selling HQ
            if (itemIsHq && !listing.IsHq)
            {
                skippedNqCount++;
                continue;
            }
                
            hasListings = true;
            validListingCount++;
            
            if (listing.PricePerUnit < lowestPrice)
            {
                if (config.DebugMode)
                    Svc.Chat.Print($"[PriceDrop Debug] Found new lowest: {listing.PricePerUnit} gil (was {lowestPrice})");
                lowestPrice = listing.PricePerUnit;
            }
        }
        
        if (config.DebugMode)
            Svc.Chat.Print($"[PriceDrop Debug] Summary - Skipped own: {skippedOwnCount}, Skipped NQ: {skippedNqCount}, Valid: {validListingCount}");
        
        if (hasListings && lowestPrice < uint.MaxValue)
        {
            // Calculate undercut price
            var undercutPrice = Math.Max(1, (int)lowestPrice - config.UndercutAmount);
            
            if (config.DebugMode)
                Svc.Chat.Print($"[PriceDrop Debug] Lowest price: {lowestPrice}, Undercut amount: {config.UndercutAmount}, Final price: {undercutPrice}");
            
            // Copy to clipboard using ImGui (like PennyPincher)
            ImGui.SetClipboardText(undercutPrice.ToString());
            lastCopyTime = DateTime.Now;
            
            Svc.Chat.Print($"[PriceDrop] Copied price to clipboard: {undercutPrice} gil");
            
            // Mark this item as processed
            lastProcessedItemName = currentItemName;
            lastItemProcessedTime = DateTime.Now;
        }
        else
        {
            if (config.DebugMode)
                Svc.Chat.Print($"[PriceDrop Debug] No valid listings found to undercut");
        }
    }
    
    private bool IsOurRetainer(string retainerName)
    {
        unsafe
        {
            var retainerManager = RetainerManager.Instance();
            if (retainerManager == null) 
            {
                if (config.DebugMode)
                    Svc.Chat.Print($"[PriceDrop Debug] RetainerManager is null, can't check if {retainerName} is ours");
                return false;
            }
            
            for (uint i = 0; i < retainerManager->GetRetainerCount(); i++)
            {
                var retainer = retainerManager->GetRetainerBySortedIndex(i);
                if (retainer == null || retainer->Town == 0) continue;
                
                var nameBytes = retainer->Name;
                var nameStr = System.Text.Encoding.UTF8.GetString(nameBytes.ToArray());
                var cleanName = nameStr.Split('\0')[0];
                
                if (cleanName.Contains("="))
                {
                    cleanName = cleanName.Substring(0, cleanName.IndexOf('='));
                }
                
                if (cleanName.Trim().Equals(retainerName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }
    }
}