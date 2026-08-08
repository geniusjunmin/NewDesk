using System.IO;
using System.Text.Json;
using NewDesk.Models;
using NewDesk.Services;
using Xunit;

namespace NewDesk.Tests;

public sealed class ReminderMigrationTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskReminderMigration_{Guid.NewGuid():N}");

    public ReminderMigrationTests() => AppEnvironment.SetTestEnvironment(_testRoot);

    public void Dispose()
    {
        AppEnvironment.ResetToNormalEnvironment();
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [Fact]
    public void RemindersV2ToV3_RepairsOnlyBackedUpLegacyAnnual()
    {
        Guid legacyId = Guid.NewGuid();
        Guid validOneTimeId = Guid.NewGuid();
        DataService.SaveReminders(
        [
            new Reminder { Id = legacyId, Title = "legacy", ScheduleType = ReminderScheduleType.OneTime, DueAt = DateTime.Now.AddDays(1), Month = 8, Day = 10 },
            new Reminder { Id = validOneTimeId, Title = "valid", ScheduleType = ReminderScheduleType.OneTime, DueAt = DateTime.Now.AddDays(2) }
        ]);
        string backupDir = Path.Combine(AppDataPath.DataFolder, "Backups", "Migration");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(
            Path.Combine(backupDir, "reminders_before_v2_20260808_000000.json"),
            $"[{{\"Id\":\"{legacyId}\",\"Title\":\"legacy\",\"Month\":8,\"Day\":10,\"IsLunar\":false}}]");

        Assert.True(new RemindersV2ToV3MigrationStep().Execute());
        var reminders = DataService.LoadReminders();
        Assert.Equal(ReminderScheduleType.Yearly, reminders.Single(item => item.Id == legacyId).ScheduleType);
        Assert.Equal(ReminderScheduleType.OneTime, reminders.Single(item => item.Id == validOneTimeId).ScheduleType);
    }

    [Fact]
    public void RemindersV2ToV3_WithoutBackup_DoesNotGuess()
    {
        var reminder = new Reminder { Id = Guid.NewGuid(), ScheduleType = ReminderScheduleType.OneTime, DueAt = DateTime.Now.AddDays(1) };
        DataService.SaveReminders([reminder]);

        Assert.True(new RemindersV2ToV3MigrationStep().Execute());

        Assert.Equal(ReminderScheduleType.OneTime, DataService.LoadReminders().Single().ScheduleType);
        Assert.NotNull(MigrationService.LastRepairWarning);
    }
}
