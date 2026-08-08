using System.Globalization;
using System.IO;
using NewDesk.Models;
using NewDesk.Services;
using Xunit;

namespace NewDesk.Tests;

public sealed class ReminderScheduleTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskReminderTests_{Guid.NewGuid():N}");

    public ReminderScheduleTests()
    {
        AppEnvironment.SetTestEnvironment(_testRoot);
        SettingsService.SaveSettings(new AppSettings { EnableNotifications = true });
        ReminderService.Stop();
    }

    public void Dispose()
    {
        ReminderService.Stop();
        ReminderService.Clock = SystemClock.Instance;
        AppEnvironment.ResetToNormalEnvironment();
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [Fact]
    public void Yearly_DueAt0900_CheckAt090005_StillNotifies()
    {
        var reminder = YearlyReminder(daysInAdvance: 0);
        var now = new DateTime(2027, 8, 10, 9, 0, 5);

        var window = ReminderScheduleCalculator.GetNotificationOccurrence(reminder, now);

        Assert.NotNull(window);
        Assert.Equal(new DateTime(2027, 8, 10, 9, 0, 0), window!.Occurrence);
    }

    [Fact]
    public void Yearly_AdvanceOnce()
    {
        var reminder = YearlyReminder(daysInAdvance: 2);
        RunService(reminder, new DateTime(2027, 8, 8, 10, 0, 0));
        DateTime? first = reminder.LastAdvanceOccurrence;

        ReminderService.CheckReminders(null);

        Assert.Equal(new DateTime(2027, 8, 10, 9, 0, 0), first);
        Assert.Equal(first, reminder.LastAdvanceOccurrence);
        Assert.Null(reminder.LastDueOccurrence);
    }

    [Fact]
    public void Yearly_DueOnce()
    {
        var reminder = YearlyReminder(daysInAdvance: 0);
        RunService(reminder, new DateTime(2027, 8, 10, 9, 0, 5));
        DateTime? first = reminder.LastDueOccurrence;

        ReminderService.CheckReminders(null);

        Assert.Equal(new DateTime(2027, 8, 10, 9, 0, 0), first);
        Assert.Equal(first, reminder.LastDueOccurrence);
    }

    [Fact]
    public void Yearly_NotCompleted()
    {
        var reminder = YearlyReminder(daysInAdvance: 0);
        RunService(reminder, new DateTime(2027, 8, 10, 9, 0, 5));
        Assert.False(reminder.IsCompleted);
    }

    [Fact]
    public void Lunar_BeforeChineseNewYear_UsesCurrentLunarYear()
    {
        var now = new DateTime(2027, 1, 20, 8, 0, 0);
        var calendar = new ChineseLunisolarCalendar();
        int currentLunarYear = calendar.GetYear(now);
        var reminder = LunarReminder(month: 12, day: 20);

        var next = ReminderScheduleCalculator.GetNextOccurrence(reminder, now);

        Assert.NotNull(next);
        Assert.Equal(currentLunarYear, calendar.GetYear(next!.Value));
        Assert.True(next > now);
    }

    [Fact]
    public void Lunar_AfterChineseNewYear()
    {
        var now = new DateTime(2027, 2, 10, 8, 0, 0);
        var calendar = new ChineseLunisolarCalendar();
        int currentLunarYear = calendar.GetYear(now);
        var reminder = LunarReminder(month: 1, day: 20);

        var next = ReminderScheduleCalculator.GetNextOccurrence(reminder, now);

        Assert.NotNull(next);
        Assert.Equal(currentLunarYear, calendar.GetYear(next!.Value));
        Assert.True(next > now);
    }

    [Fact]
    public void ReminderService_StartTwice_OnlyOneTimerActive()
    {
        ReminderService.Start([], ReminderFrequency.Minutely);
        ReminderService.Start([], ReminderFrequency.Minutely);
        Assert.Equal(1, ReminderService.ActiveTimerCount);
    }

    [Fact]
    public void OneTime_DueOnlyOnce()
    {
        var due = new DateTime(2027, 8, 10, 9, 0, 0);
        var reminder = new Reminder { Title = "one", ScheduleType = ReminderScheduleType.OneTime, DueAt = due };
        RunService(reminder, due.AddSeconds(5));
        DateTime? notifiedAt = reminder.DueNotifiedAt;

        ReminderService.CheckReminders(null);

        Assert.True(reminder.IsCompleted);
        Assert.Equal(notifiedAt, reminder.DueNotifiedAt);
    }

    [Fact]
    public void NotificationsDisabled_DoesNotMarkAsNotified()
    {
        SettingsService.SaveSettings(new AppSettings { EnableNotifications = false });
        var reminder = YearlyReminder(daysInAdvance: 0);

        RunService(reminder, new DateTime(2027, 8, 10, 9, 0, 5));

        Assert.Null(reminder.LastDueOccurrence);
        Assert.Equal(default, reminder.LastNotifiedDate);
    }

    [Fact]
    public void InvalidTime_2500_Rejected()
    {
        Assert.False(ReminderTimeParser.TryParseClockTime("25:00", out _));
        Assert.False(ReminderTimeParser.TryParseClockTime("12:99", out _));
        Assert.False(ReminderTimeParser.TryParseClockTime("abc", out _));
        Assert.True(ReminderTimeParser.TryParseClockTime("9:05", out var parsed));
        Assert.Equal(new TimeSpan(9, 5, 0), parsed);
    }

    private static Reminder YearlyReminder(int daysInAdvance) => new()
    {
        Title = "yearly",
        ScheduleType = ReminderScheduleType.Yearly,
        Month = 8,
        Day = 10,
        TimeOfDay = new TimeSpan(9, 0, 0),
        DaysInAdvance = daysInAdvance
    };

    private static Reminder LunarReminder(int month, int day) => new()
    {
        Title = "lunar",
        ScheduleType = ReminderScheduleType.LunarYearly,
        Month = month,
        Day = day,
        TimeOfDay = new TimeSpan(9, 0, 0)
    };

    private static void RunService(Reminder reminder, DateTime now)
    {
        ReminderService.Clock = new TestClock(now);
        ReminderService.SetRemindersForTesting([reminder]);
        ReminderService.CheckReminders(null);
    }
}
