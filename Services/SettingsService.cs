using System;
using System.IO;
using System.Text.Json;
using NewDesk.Models;

namespace NewDesk.Services;

public static class SettingsService
{
    public static AppSettings LoadSettings()
    {
        AppDataPath.Initialize();
        string path = AppDataPath.SettingsFile;
        if (!File.Exists(path))
        {
            string oldPath = Path.Combine(AppContext.BaseDirectory, "app_settings.json");
            if (File.Exists(oldPath))
            {
                path = oldPath;
            }
            else
            {
                return new AppSettings();
            }
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("SettingsService.LoadSettings", ex);
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        AppDataPath.Initialize();
        try
        {
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppDataPath.SettingsFile, json);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("SettingsService.SaveSettings", ex);
            throw new IOException($"设置保存失败: {ex.Message}", ex);
        }
    }
}
