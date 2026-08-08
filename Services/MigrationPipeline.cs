using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
            settings.LastRunVersion = "2.2.2";
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
            var reminders = DataService.LoadReminders();
            foreach (var r in reminders)
            {
                if (!r.DueAt.HasValue)
                {
                    int hours = r.TimeOfDay?.Hours ?? 9;
                    int minutes = r.TimeOfDay?.Minutes ?? 0;
                    r.DueAt = new DateTime(DateTime.Now.Year, r.Month, r.Day, hours, minutes, 0);
                }
            }
            DataService.SaveReminders(reminders);
            return true;
        }
        catch { return false; }
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
