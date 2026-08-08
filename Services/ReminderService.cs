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
    private static Timer? _timer;
    private static List<Reminder> _reminders = new();
    private static ReminderFrequency _frequency;
    public static IClock Clock { get; set; } = SystemClock.Instance;

    public static void Start(List<Reminder> reminders, ReminderFrequency frequency)
    {
        _reminders = reminders;
        _frequency = frequency;
        _timer = new Timer(CheckReminders, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    public static void Stop()
    {
        _timer?.Dispose();
    }

    public static void CheckReminders(object? state)
    {
        DateTime now = Clock.Now;
        foreach (var reminder in _reminders.ToList())
        {
            if (reminder.IsCompleted) continue;

            // Handle Snooze
            if (reminder.SnoozeUntil.HasValue)
            {
                if (now < reminder.SnoozeUntil.Value)
                {
                    continue;
                }
                else
                {
                    reminder.SnoozeUntil = null;
                    NotifyAndSave(reminder, now, "Snooze 提醒时间已到！");
                    continue;
                }
            }

            var nextOccurrence = ReminderScheduleCalculator.GetNextOccurrence(reminder, now);
            if (!nextOccurrence.HasValue) continue;

            reminder.NextReminderDate = nextOccurrence.Value;

            if (reminder.ScheduleType == ReminderScheduleType.OneTime)
            {
                DateTime due = nextOccurrence.Value;

                // 1. Check Advance Notification
                if (reminder.DaysInAdvance > 0 && reminder.AdvanceNotifiedAt == null)
                {
                    DateTime advanceStart = due.AddDays(-reminder.DaysInAdvance);
                    if (now >= advanceStart && now < due)
                    {
                        reminder.AdvanceNotifiedAt = now;
                        NotifyAndSave(reminder, now, $"提前 {reminder.DaysInAdvance} 天提醒！");
                        continue;
                    }
                }

                // 2. Check Final Due Notification
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
                DateTime occurrenceDate = nextOccurrence.Value.Date;

                // 1. Check Advance Notification per occurrence
                if (reminder.DaysInAdvance > 0 && reminder.LastAdvanceOccurrence?.Date != occurrenceDate)
                {
                    DateTime advanceStart = nextOccurrence.Value.AddDays(-reminder.DaysInAdvance);
                    if (now >= advanceStart && now < nextOccurrence.Value)
                    {
                        reminder.LastAdvanceOccurrence = occurrenceDate;
                        NotifyAndSave(reminder, now, $"提前 {reminder.DaysInAdvance} 天提醒！");
                        continue;
                    }
                }

                // 2. Check Final Due Notification per occurrence
                if (reminder.LastDueOccurrence?.Date != occurrenceDate && now >= nextOccurrence.Value)
                {
                    reminder.LastDueOccurrence = occurrenceDate;
                    NotifyAndSave(reminder, now, $"提醒时间已到 ({reminder.Title})！");
                    continue;
                }
            }
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
