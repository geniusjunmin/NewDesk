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
    private static readonly ChineseLunisolarCalendar LunarCalendar = new();
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
                    // Trigger Snooze Notification once and clear snooze state
                    reminder.SnoozeUntil = null;
                    NotifyAndSave(reminder, now, "Snooze 提醒时间已到！");
                    continue;
                }
            }

            if (reminder.ScheduleType == ReminderScheduleType.OneTime && reminder.DueAt.HasValue)
            {
                DateTime due = reminder.DueAt.Value;

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
                UpdateNextReminderDate(reminder);

                if (now.Date >= reminder.NextReminderDate.AddDays(-reminder.DaysInAdvance))
                {
                    if (reminder.LastNotifiedDate.Date != now.Date)
                    {
                        reminder.LastNotifiedDate = now;
                        NotifyAndSave(reminder, now, $"今天是 {reminder.NextReminderDate:yyyy-MM-dd}！");
                    }
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
            : $"[{statusText}] 目标日期: {reminder.NextReminderDate:yyyy-MM-dd}";

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
        DateTime today = Clock.Now.Date;
        int currentYear = today.Year;

        DateTime reminderDateThisYear;
        try
        {
            if (reminder.IsLunar)
            {
                int leapMonth = LunarCalendar.GetLeapMonth(currentYear);
                int month = reminder.Month;
                if (leapMonth > 0 && month >= leapMonth)
                {
                    month++;
                }
                reminderDateThisYear = LunarCalendar.ToDateTime(currentYear, month, reminder.Day, 0, 0, 0, 0);
            }
            else
            {
                reminderDateThisYear = new DateTime(currentYear, reminder.Month, reminder.Day);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            if (reminder.Month == 2 && reminder.Day == 29)
            {
                reminderDateThisYear = new DateTime(currentYear, 2, 28);
            }
            else
            {
                int daysInMonth = CultureInfo.CurrentCulture.Calendar.GetDaysInMonth(currentYear, reminder.Month);
                reminderDateThisYear = new DateTime(currentYear, reminder.Month, daysInMonth);
            }
        }

        if (reminderDateThisYear.AddDays(-reminder.DaysInAdvance) < today)
        {
            reminder.NextReminderDate = GetNextYearDate(reminder, currentYear + 1);
        }
        else
        {
            reminder.NextReminderDate = reminderDateThisYear;
        }
    }

    private static DateTime GetNextYearDate(Reminder reminder, int year)
    {
        try
        {
            if (reminder.IsLunar)
            {
                int leapMonth = LunarCalendar.GetLeapMonth(year);
                int month = reminder.Month;
                if (leapMonth > 0 && month >= leapMonth)
                {
                    month++;
                }
                return LunarCalendar.ToDateTime(year, month, reminder.Day, 0, 0, 0, 0);
            }
            else
            {
                return new DateTime(year, reminder.Month, reminder.Day);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return GetNextYearDate(reminder, year + 1);
        }
    }
}
