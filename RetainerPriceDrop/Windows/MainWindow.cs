using Dalamud.Interface.Windowing;
using RetainerPriceDrop.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using ECommons.DalamudServices;
using System.Numerics;
using Dalamud.Interface.Utility;
using System;
using Dalamud.Interface.Colors;

namespace RetainerPriceDrop.Windows;

public class MainWindow : Window, IDisposable
{
    private RetainerPriceController controller;
    private RetainerTracker tracker;
    private Configuration config;
    
    public MainWindow(RetainerPriceController controller, RetainerTracker tracker, Configuration config) : base(
        "Retainer Price Drop")
    {
        Size = new Vector2(650, 450);
        SizeCondition = ImGuiCond.Appearing;
        Flags = ImGuiWindowFlags.None;
        Position = new Vector2(100, 100);
        PositionCondition = ImGuiCond.FirstUseEver;
        
        this.controller = controller;
        this.tracker = tracker;
        this.config = config;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (!controller.IsRunning)
        {
            // Show retainer table if RetainerList is open or retainers are available
            unsafe
            {
                var retainerManager = RetainerManager.Instance();
                if (retainerManager != null && retainerManager->GetRetainerCount() > 0)
                {
                    
                    // Create table with columns
                    if (ImGui.BeginTable("RetainerTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX))
                    {
                        // Setup columns - Name column much smaller, others adjusted
                        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 100);
                        ImGui.TableSetupColumn("Items on Market", ImGuiTableColumnFlags.WidthFixed, 110);
                        ImGui.TableSetupColumn("Next Price Drop", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 100);
                        ImGui.TableHeadersRow();
                        
                        for (uint i = 0; i < retainerManager->GetRetainerCount(); i++)
                        {
                            var retainer = retainerManager->GetRetainerBySortedIndex(i);
                            if (retainer != null)
                            {
                                // Skip deactivated retainers (Town value of 0 means deactivated)
                                if (retainer->Town == 0)
                                {
                                    continue;
                                }
                                
                                // Properly decode the retainer name from the byte array
                                var nameBytes = retainer->Name;
                                
                                // Convert to string and clean up
                                var nameStr = System.Text.Encoding.UTF8.GetString(nameBytes.ToArray());
                                
                                // Remove null terminators and any trailing garbage
                                var cleanName = nameStr.Split('\0')[0]; // Take everything before first null
                                
                                // Some retainers have =R or other suffixes, remove them
                                if (cleanName.Contains("="))
                                {
                                    cleanName = cleanName.Substring(0, cleanName.IndexOf('='));
                                }
                                
                                var name = cleanName.Trim();
                                
                                if (!string.IsNullOrEmpty(name))
                                {
                                    ImGui.TableNextRow();
                                    
                                    // Consistent vertical padding for all columns
                                    float rowPadding = 3.0f;
                                    
                                    // Name column
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + rowPadding);
                                    ImGui.Text(name);
                                    
                                    // Items on Market column
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + rowPadding);
                                    if (retainer->MarketItemCount > 0)
                                    {
                                        ImGui.Text($"{retainer->MarketItemCount} items");
                                    }
                                    else
                                    {
                                        ImGui.TextColored(ImGuiColors.DalamudGrey, "No items");
                                    }
                                    
                                    // Price Drop Available column
                                    ImGui.TableSetColumnIndex(2);
                                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + rowPadding);
                                    var lastDrop = tracker.GetLastPriceDrop(name);
                                    if (lastDrop.HasValue)
                                    {
                                        var timeSince = DateTime.Now - lastDrop.Value;
                                        var timeUntilAvailable = TimeSpan.FromHours(18) - timeSince;
                                        string timeText;
                                        
                                        if (timeUntilAvailable.TotalSeconds <= 0)
                                        {
                                            // Can update now
                                            timeText = "Available Now";
                                            ImGui.TextColored(new Vector4(0.0f, 0.8f, 0.0f, 1.0f), timeText);
                                        }
                                        else
                                        {
                                            // Still on cooldown
                                            int hours = (int)timeUntilAvailable.TotalHours;
                                            int minutes = timeUntilAvailable.Minutes;
                                            int seconds = timeUntilAvailable.Seconds;
                                            
                                            timeText = $"Available in {hours:D2}:{minutes:D2}:{seconds:D2}";
                                            
                                            ImGui.Text(timeText);
                                        }
                                        
                                        // Show tooltip with exact time when available on hover
                                        if (ImGui.IsItemHovered())
                                        {
                                            var availableTime = lastDrop.Value.AddHours(18);
                                            ImGui.SetTooltip($"Available at: {availableTime:yyyy-MM-dd HH:mm:ss}");
                                        }
                                    }
                                    else
                                    {
                                        ImGui.TextColored(new Vector4(0.0f, 0.8f, 0.0f, 1.0f), "Available Now");
                                    }
                                    
                                    // Actions column
                                    ImGui.TableSetColumnIndex(3);
                                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + rowPadding);
                                    
                                    // Center the button horizontally in the column
                                    float buttonWidth = 85;
                                    float columnWidth = ImGui.GetContentRegionAvail().X;
                                    float centerOffset = (columnWidth - buttonWidth) * 0.5f;
                                    if (centerOffset > 0)
                                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerOffset);
                                    
                                    // Check if prices were recently dropped
                                    bool hasItems = retainer->MarketItemCount > 0;
                                    bool recentlyUpdated = false;
                                    TimeSpan? timeSinceUpdate = null;
                                    
                                    if (hasItems && lastDrop.HasValue)
                                    {
                                        timeSinceUpdate = DateTime.Now - lastDrop.Value;
                                        recentlyUpdated = timeSinceUpdate.Value.TotalHours < 18;
                                    }
                                    
                                    // Disable button if no items OR recently updated
                                    if (!hasItems || recentlyUpdated)
                                    {
                                        ImGui.BeginDisabled();
                                    }
                                    else
                                    {
                                        // Light blue color for normal enabled buttons
                                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.8f, 1.0f));
                                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 0.9f, 1.0f));
                                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.4f, 0.7f, 1.0f));
                                    }
                                    
                                    if (ImGui.Button($"Drop Prices##retainer{i}", new Vector2(buttonWidth, 0)))
                                    {
                                        controller.StartForRetainer((int)i, name);
                                    }
                                    
                                    if (!hasItems || recentlyUpdated)
                                    {
                                        ImGui.EndDisabled();
                                        
                                        // Show appropriate tooltip when hovering over disabled button
                                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                                        {
                                            if (!hasItems)
                                            {
                                                ImGui.SetTooltip("No items listed on market");
                                            }
                                            else if (recentlyUpdated && timeSinceUpdate.HasValue)
                                            {
                                                int hoursAgo = (int)timeSinceUpdate.Value.TotalHours;
                                                ImGui.SetTooltip($"Prices dropped {hoursAgo} hours ago. Please wait at least 18 hours between price updates.");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        ImGui.PopStyleColor(3);
                                    }
                                }
                            }
                        }
                        
                        ImGui.EndTable();
                    }
                    
                    // Add undercut amount configuration
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                    
                    // Add debug mode checkbox on the left
                    bool debugMode = config.DebugMode;
                    if (ImGui.Checkbox("Debug Mode", ref debugMode))
                    {
                        config.DebugMode = debugMode;
                        config.Save();
                        Svc.Chat.Print($"Debug mode {(debugMode ? "enabled" : "disabled")}");
                    }
                    
                    ImGui.SameLine();
                    
                    // Right-align the undercut section
                    float contentWidth = 200 + 80 + 25 + 25; // label + input + button + button
                    float fullWindowWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                    ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMin().X + fullWindowWidth - contentWidth);
                    
                    ImGui.Text("Amount (gil) to undercut by:");
                    ImGui.SameLine();
                    
                    ImGui.SetNextItemWidth(80);
                    int undercutAmount = config.UndercutAmount;
                    if (ImGui.InputInt("##undercutAmount", ref undercutAmount, 0, 0))
                    {
                        if (undercutAmount < 1) undercutAmount = 1;
                        if (undercutAmount > 999999) undercutAmount = 999999;
                        config.UndercutAmount = undercutAmount;
                        config.Save();
                        
                        // Send chat message
                        Svc.Chat.Print($"Saved: Undercut by {undercutAmount} gil");
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button(" - ##decrease", new Vector2(25, 0)))
                    {
                        if (config.UndercutAmount > 1)
                        {
                            config.UndercutAmount--;
                            config.Save();
                            Svc.Chat.Print($"Saved: Undercut by {config.UndercutAmount} gil");
                        }
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button(" + ##increase", new Vector2(25, 0)))
                    {
                        if (config.UndercutAmount < 999999)
                        {
                            config.UndercutAmount++;
                            config.Save();
                            Svc.Chat.Print($"Saved: Undercut by {config.UndercutAmount} gil");
                        }
                    }
                }
                else
                {
                    ImGui.TextColored(ImGuiColors.DalamudGrey, "No retainers available. Please interact with a retainer bell.");
                }
            }
        }
        else
        {
            // Show status during automation
            ImGui.Text($"Status: {controller.Status}");
            ImGui.Text($"Items Processed: {controller.ItemsProcessed}");
            ImGui.Spacing();
            
            if (controller.IsPaused)
            {
                ImGui.TextColored(ImGuiColors.DalamudOrange, "Automation Paused");
            }
            else
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, "Automation Running...");
            }
            
            ImGui.Spacing();
            
            if (!controller.IsPaused)
            {
                if (ImGui.Button("Pause", new Vector2(100, 30)))
                {
                    controller.Pause();
                }
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudGrey3, "Pause to manually click items");
            }
            else
            {
                if (ImGui.Button("Resume", new Vector2(100, 30)))
                {
                    controller.Resume();
                }
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudYellow, "Click items manually while paused");
            }
            
            ImGui.Spacing();
            
            if (ImGui.Button("Stop", new Vector2(150, 30)))
            {
                controller.Stop();
            }
        }
    }
}