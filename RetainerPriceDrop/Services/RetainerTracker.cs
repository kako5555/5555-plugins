using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin;
using ECommons.DalamudServices;

namespace RetainerPriceDrop.Services;

public class RetainerTracker
{
    private readonly string saveFilePath;
    private Dictionary<string, DateTime> lastPriceDropTimes = new();
    
    public RetainerTracker(IDalamudPluginInterface pluginInterface)
    {
        saveFilePath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "retainer_timestamps.json");
        LoadTimestamps();
    }
    
    public void RecordPriceDrop(string retainerName)
    {
        lastPriceDropTimes[retainerName] = DateTime.Now;
        SaveTimestamps();
    }
    
    public DateTime? GetLastPriceDrop(string retainerName)
    {
        return lastPriceDropTimes.TryGetValue(retainerName, out var time) ? time : null;
    }
    
    private void LoadTimestamps()
    {
        try
        {
            if (File.Exists(saveFilePath))
            {
                var json = File.ReadAllText(saveFilePath);
                lastPriceDropTimes = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? new Dictionary<string, DateTime>();
            }
            else
            {
                lastPriceDropTimes = new Dictionary<string, DateTime>();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to load retainer timestamps: {ex}");
            lastPriceDropTimes = new Dictionary<string, DateTime>();
        }
    }
    
    private void SaveTimestamps()
    {
        try
        {
            var directory = Path.GetDirectoryName(saveFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(lastPriceDropTimes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(saveFilePath, json);
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"Failed to save retainer timestamps: {ex}");
        }
    }
}