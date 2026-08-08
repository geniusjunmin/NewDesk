using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using NewDesk.Models;

namespace NewDesk.Services;

public static class ReminderService
{
    private static readonly object LifecycleLock = new();
    private static Timer? _timer;
    private static List<Reminder> _reminders = new();
    private static ReminderFrequency _frequency;
    private static int _isChecking;
    public static IClock Clock { get; set; } = SystemClock.Instance;
    public static int ActiveTimerCount => Volatile.Read(ref _timer) == null ? 0 : 1;

    public static void Start(List<Reminder> reminders, ReminderFrequency frequency)
    {
        lock (LifecycleLock)
        {
            StopCore();
            _reminders = reminders;
            _frequency = frequency;
            _timer = new Timer(CheckReminders, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }
    }

    public static void Stop()
    {
        lock (LifecycleLock)
        {
            StopCore();
        }
    }

    private static void StopCore()
    {
        Interlocked.Exchange(ref _timer, null)?.Dispose();
    }

    internal static void SetRemindersForTesting(List<Reminder> reminders)
    {
        _reminders = reminders;
    }

    public static void CheckReminders(object? state)
    {
        if (Interlocked.Exchange(ref _isChecking, 1) != 0)
        {
            return;
        }

        try
        {
            DateTime now = Clock.Now;
            bool notificationsEnabled = SettingsService.LoadSettings().EnableNotifications;
            foreach (var reminder in _reminders.ToList())
            {
                if (reminder.IsCompleted) continue;

                var nextOccurrence = ReminderScheduleCalculator.GetNextOccurrence(reminder, now);
                if (nextOccurrence.HasValue)
                {
                    reminder.NextReminderDate = nextOccurrence.Value;
                }

                if (!notificationsEnabled)
                {
                    continue;
                }

                if (reminder.SnoozeUntil.HasValue)
                {
                    if (now < reminder.SnoozeUntil.Value)
                    {
                        continue;
                    }

                    reminder.SnoozeUntil = null;
                    NotifyAndSave(reminder, now, "Snooze 提醒时间已到！");
                    continue;
                }

                var notificationWindow = ReminderScheduleCalculator.GetNotificationOccurrence(reminder, now);
                if (notificationWindow == null) continue;

                if (reminder.ScheduleType == ReminderScheduleType.OneTime)
                {
                    DateTime due = notificationWindow.Occurrence;
                    if (reminder.DaysInAdvance > 0 && reminder.AdvanceNotifiedAt == null &&
                        now >= notificationWindow.AdvanceStart && now < due)
                    {
                        reminder.AdvanceNotifiedAt = now;
                        NotifyAndSave(reminder, now, $"提前 {reminder.DaysInAdvance} 天提醒！");
                        continue;
                    }

                    if (reminder.DueNotifiedAt == null && now >= due)
                    {
                        reminder.DueNotifiedAt = now;
                        reminder.IsCompleted = true;
                        NotifyAndSave(reminder, now, "提醒时间已到！");
                        continue;
                    }
                }
                else
                {
                    DateTime occurrence = notificationWindow.Occurrence;

                    if (reminder.DaysInAdvance > 0 && reminder.LastAdvanceOccurrence != occurrence &&
                        now >= notificationWindow.AdvanceStart && now < occurrence)
                    {
                        reminder.LastAdvanceOccurrence = occurrence;
                        NotifyAndSave(reminder, now, $"提前 {reminder.DaysInAdvance} 天提醒！");
                        continue;
                    }

                    if (reminder.LastDueOccurrence != occurrence && now >= occurrence)
                    {
                        reminder.LastDueOccurrence = occurrence;
                        NotifyAndSave(reminder, now, $"提醒时间已到 ({reminder.Title})！");
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    private static void NotifyAndSave(Reminder reminder, DateTime now, string statusText)
    {
        reminder.LastNotifiedDate = now;

        if (Application.Current != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ShowReminderToast(reminder, statusText);
                DataService.SaveReminders(_reminders);
            });
        }
        else
        {
            DataService.SaveReminders(_reminders);
        }
    }

    private static void ShowReminderToast(Reminder reminder, string statusText)
    {
        string message = reminder.DueAt.HasValue
            ? $"[{statusText}] 设定时间: {reminder.DueAt.Value:yyyy-MM-dd HH:mm}"
            : $"[{statusText}] 目标日期: {reminder.NextReminderDate:yyyy-MM-dd HH:mm}";

        var toast = new ReminderToastWindow(reminder.Title, message, autoClose: false);
        toast.Show();
    }

    public static void ShowTestReminder()
    {
        var toast = new ReminderToastWindow("测试提醒", "这是一个测试提醒通知。如果看到此消息，说明提醒功能工作正常。", autoClose: false);
        toast.Show();
    }

    public static void UpdateNextReminderDate(Reminder reminder)
    {
        var next = ReminderScheduleCalculator.GetNextOccurrence(reminder, Clock.Now);
        if (next.HasValue)
        {
            reminder.NextReminderDate = next.Value;
        }
    }
}
