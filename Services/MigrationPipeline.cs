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
            settings.LastRunVersion = AppVersionService.Version;
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

public class RemindersV2ToV3MigrationStep : IMigrationStep
{
    public string Domain => "Reminders";
    public int FromVersion => 2;
    public int ToVersion => 3;

    public bool Execute()
    {
        try
        {
            var reminders = DataService.LoadReminders();
            string backupDir = Path.Combine(AppDataPath.DataFolder, "Backups", "Migration");
            string? backup = Directory.Exists(backupDir)
                ? Directory.GetFiles(backupDir, "reminders_before_v2_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;

            if (backup == null)
            {
                MigrationService.LastRepairWarning = "检测到旧提醒可能受 2.2.2 迁移影响，请检查提醒类型。";
                AppDataPath.LogInfo($"MigrationRepairWarning: {MigrationService.LastRepairWarning}");
                return true;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(backup));
            var legacyById = new Dictionary<Guid, JsonElement>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("Id", out var idElement) && idElement.TryGetGuid(out var id))
                {
                    legacyById[id] = element.Clone();
                }
            }

            bool changed = false;
            foreach (var reminder in reminders.Where(item => item.ScheduleType == ReminderScheduleType.OneTime))
            {
                if (!legacyById.TryGetValue(reminder.Id, out var legacy)) continue;
                bool hasScheduleType = legacy.TryGetProperty("ScheduleType", out _);
                bool hasDueAt = legacy.TryGetProperty("DueAt", out var dueAt) && dueAt.ValueKind != JsonValueKind.Null;
                if (hasScheduleType || hasDueAt) continue;

                bool isLunar = legacy.TryGetProperty("IsLunar", out var lunar) && lunar.ValueKind == JsonValueKind.True;
                reminder.ScheduleType = isLunar ? ReminderScheduleType.LunarYearly : ReminderScheduleType.Yearly;
                reminder.IsLunar = isLunar;
                reminder.DueAt = null;
                reminder.TimeOfDay ??= new TimeSpan(9, 0, 0);
                changed = true;
            }

            if (changed) DataService.SaveReminders(reminders);
            return true;
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("RemindersV2ToV3MigrationStep", ex);
            return false;
        }
    }
}

public class MigrationPipeline
{
    private static readonly List<IMigrationStep> Steps = new()
    {
        new SettingsV1ToV2MigrationStep(),
        new RemindersV1ToV2MigrationStep(),
        new RemindersV2ToV3MigrationStep()
    };

    public static List<IMigrationStep> GetSteps() => Steps;
}
