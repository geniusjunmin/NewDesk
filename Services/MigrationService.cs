using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NewDesk.Services;

public class MigrationState
{
    public int Settings { get; set; } = 1;
    public int Reminders { get; set; } = 1;
    public int Wallpapers { get; set; } = 1;
    public int DynamicData { get; set; } = 1;
    public int AI { get; set; } = 1;
}

public static class MigrationService
{
    public const int TargetSchemaVersion = 3;
    private static string StateFilePath => Path.Combine(AppDataPath.DataFolder, "migration_state.json");

    public static void RunAllMigrationsIfNeeded()
    {
        try
        {
            AppDataPath.Initialize();
            var state = LoadMigrationState();

            bool stateChanged = false;

            if (state.DynamicData < TargetSchemaVersion)
            {
                DynamicDataService.LoadSources();
                state.DynamicData = TargetSchemaVersion;
                stateChanged = true;
            }

            if (state.Settings < TargetSchemaVersion)
            {
                SettingsService.LoadSettings();
                state.Settings = TargetSchemaVersion;
                stateChanged = true;
            }

            if (state.Reminders < TargetSchemaVersion)
            {
                DataService.LoadReminders();
                state.Reminders = TargetSchemaVersion;
                stateChanged = true;
            }

            if (state.Wallpapers < TargetSchemaVersion)
            {
                WallpaperService.LoadWallpapers();
                state.Wallpapers = TargetSchemaVersion;
                stateChanged = true;
            }

            if (stateChanged)
            {
                SaveMigrationState(state);
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("MigrationService.RunAllMigrationsIfNeeded", ex);
        }
    }

    private static MigrationState LoadMigrationState()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                string json = File.ReadAllText(StateFilePath);
                return JsonSerializer.Deserialize<MigrationState>(json) ?? new MigrationState();
            }
        }
        catch { }
        return new MigrationState();
    }

    private static void SaveMigrationState(MigrationState state)
    {
        try
        {
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            SafeFileWriter.WriteAllText(StateFilePath, json);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("MigrationService.SaveMigrationState", ex);
        }
    }
}
