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
    public int Passwords { get; set; } = 1;
    public int AI { get; set; } = 1;
}

public class MigrationRunResult
{
    public bool Success { get; set; } = true;
    public List<string> FailedDomains { get; set; } = new();
    public List<string> CompletedSteps { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public static class MigrationService
{
    public static string? LastRepairWarning { get; internal set; }
    public static readonly Dictionary<string, int> TargetVersions = new()
    {
        { "Settings", 2 },
        { "Reminders", 3 },
        { "Wallpapers", 1 },
        { "DynamicData", 1 },
        { "Passwords", 1 },
        { "AI", 1 }
    };

    private static string StateFilePath => Path.Combine(AppDataPath.DataFolder, "migration_state.json");

    public static MigrationRunResult RunAllMigrationsIfNeeded()
    {
        LastRepairWarning = null;
        var result = new MigrationRunResult();
        try
        {
            AppDataPath.Initialize();
            var state = LoadMigrationState();

            bool stateChanged = false;

            foreach (var step in MigrationPipeline.GetSteps())
            {
                int currentVer = GetDomainVersion(state, step.Domain);
                if (currentVer == step.FromVersion)
                {
                    AppDataPath.LogInfo($"Executing migration step for {step.Domain} ({step.FromVersion} -> {step.ToVersion})");
                    bool ok = step.Execute();
                    if (ok)
                    {
                        SetDomainVersion(state, step.Domain, step.ToVersion);
                        stateChanged = true;
                        result.CompletedSteps.Add($"{step.Domain} ({step.FromVersion}->{step.ToVersion})");
                    }
                    else
                    {
                        result.Success = false;
                        result.FailedDomains.Add(step.Domain);
                        AppDataPath.LogError($"Migration step for {step.Domain} failed. State version remaining at {currentVer}.", new Exception("Migration Failed"));
                    }
                }
            }

            if (stateChanged)
            {
                SaveMigrationState(state);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            AppDataPath.LogError("MigrationService.RunAllMigrationsIfNeeded", ex);
        }
        return result;
    }

    public static MigrationRunResult ReconcileAfterRestore(MigrationState? restoredVersions)
    {
        SaveMigrationState(restoredVersions ?? new MigrationState());
        return RunAllMigrationsIfNeeded();
    }

    private static int GetDomainVersion(MigrationState state, string domain) => domain switch
    {
        "Settings" => state.Settings,
        "Reminders" => state.Reminders,
        "Wallpapers" => state.Wallpapers,
        "DynamicData" => state.DynamicData,
        "Passwords" => state.Passwords,
        "AI" => state.AI,
        _ => 1
    };

    private static void SetDomainVersion(MigrationState state, string domain, int version)
    {
        switch (domain)
        {
            case "Settings": state.Settings = version; break;
            case "Reminders": state.Reminders = version; break;
            case "Wallpapers": state.Wallpapers = version; break;
            case "DynamicData": state.DynamicData = version; break;
            case "Passwords": state.Passwords = version; break;
            case "AI": state.AI = version; break;
        }
    }

    public static MigrationState LoadMigrationState()
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

    internal static void SaveMigrationState(MigrationState state)
    {
        try
        {
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            SafeFileWriter.WriteAllText(StateFilePath, json);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("MigrationService.SaveMigrationState", ex);
            throw;
        }
    }
}
