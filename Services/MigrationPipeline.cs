using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NewDesk.Models;

namespace NewDesk.Services;

public interface IMigrationStep
{
    string Domain { get; }
    int FromVersion { get; }
    int ToVersion { get; }
    bool Execute();
}

public class SettingsV1ToV2MigrationStep : IMigrationStep
{
    public string Domain => "Settings";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public bool Execute()
    {
        try
        {
            var settings = SettingsService.LoadSettings();
            settings.LastRunVersion = "2.2.3";
            SettingsService.SaveSettings(settings);
            return true;
        }
        catch { return false; }
    }
}

public class RemindersV1ToV2MigrationStep : IMigrationStep
{
    public string Domain => "Reminders";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public bool Execute()
    {
        try
        {
            string remindersPath = AppDataPath.RemindersFile;
            if (!File.Exists(remindersPath)) return true;

            // 1. Create Migration Backup ZIP/JSON before migrating
            string backupDir = Path.Combine(AppDataPath.DataFolder, "Backups", "Migration");
            Directory.CreateDirectory(backupDir);
            string backupFile = Path.Combine(backupDir, $"reminders_before_v2_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.Copy(remindersPath, backupFile, overwrite: true);

            // 2. Read raw JsonDocument to detect legacy schema without ScheduleType/DueAt
            string json = File.ReadAllText(remindersPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return true;

            var reminders = DataService.LoadReminders();
            int idx = 0;

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                if (idx >= reminders.Count) break;
                var reminderObj = reminders[idx];

                bool hasScheduleType = elem.TryGetProperty("ScheduleType", out _);
                bool hasDueAt = elem.TryGetProperty("DueAt", out var dueProp) && dueProp.ValueKind != JsonValueKind.Null;

                if (!hasScheduleType && !hasDueAt)
                {
                    // Legacy annual reminder
                    bool isLunar = elem.TryGetProperty("IsLunar", out var l) && l.GetBoolean();
                    reminderObj.ScheduleType = isLunar ? ReminderScheduleType.LunarYearly : ReminderScheduleType.Yearly;
                    reminderObj.IsLunar = isLunar;
                    reminderObj.DueAt = null;
                    reminderObj.TimeOfDay ??= new TimeSpan(9, 0, 0);
                }
                idx++;
            }

            DataService.SaveReminders(reminders);
            return true;
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("RemindersV1ToV2MigrationStep", ex);
            return false;
        }
    }
}

public class MigrationPipeline
{
    private static readonly List<IMigrationStep> Steps = new()
    {
        new SettingsV1ToV2MigrationStep(),
        new RemindersV1ToV2MigrationStep()
    };

    public static List<IMigrationStep> GetSteps() => Steps;
}
