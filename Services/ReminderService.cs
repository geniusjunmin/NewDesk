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

    private static void CheckReminders(object? state)
    {
        DateTime now = DateTime.Now;
        foreach (var reminder in _reminders.ToList()) // Use ToList() to create a copy for safe iteration
        {
            bool shouldNotify = false;
            switch (_frequency)
            {
                case ReminderFrequency.OnceADay:
                    if (reminder.LastNotifiedDate.Date != now.Date) shouldNotify = true;
                    break;
                case ReminderFrequency.Hourly:
                    if (reminder.LastNotifiedDate.Date != now.Date || reminder.LastNotifiedDate.Hour != now.Hour) shouldNotify = true;
                    break;
                case ReminderFrequency.HalfHourly:
                    if (reminder.LastNotifiedDate.Date != now.Date || reminder.LastNotifiedDate.Hour != now.Hour || (now.Minute >= 30 && reminder.LastNotifiedDate.Minute < 30) || (now.Minute < 30 && reminder.LastNotifiedDate.Minute >= 30)) shouldNotify = true;
                    break;
                case ReminderFrequency.Minutely:
                    shouldNotify = true;
                    break;
            }

            if (!shouldNotify) continue;

            UpdateNextReminderDate(reminder);

            if (now.Date >= reminder.NextReminderDate.AddDays(-reminder.DaysInAdvance))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowReminderToast(reminder);
                    reminder.LastNotifiedDate = now;
                    DataService.SaveReminders(_reminders);
                });
            }
        }
    }

    private static void ShowReminderToast(Reminder reminder)
    {
        var toast = new ReminderToastWindow(reminder.Title, $"今天是 {reminder.NextReminderDate:yyyy-MM-dd}！", autoClose: false);
        toast.Show();
    }

    public static void ShowTestReminder()
    {
        var toast = new ReminderToastWindow("测试提醒", "这是一个测试提醒通知。如果看到此消息，说明提醒功能工作正常。", autoClose: false);
        toast.Show();
    }

    public static void UpdateNextReminderDate(Reminder reminder)
    {
        DateTime today = DateTime.Today;
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
                    month++; // Adjust for leap month
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
            // Handle cases where the date is invalid for the current year (e.g., Feb 29 on a non-leap year)
            if (reminder.Month == 2 && reminder.Day == 29)
            {
                reminderDateThisYear = new DateTime(currentYear, 2, 28);
            }
            else
            {
                // For other invalid dates, just use the end of the month
                int daysInMonth = CultureInfo.CurrentCulture.Calendar.GetDaysInMonth(currentYear, reminder.Month);
                reminderDateThisYear = new DateTime(currentYear, reminder.Month, daysInMonth);
            }
        }

        if (reminderDateThisYear.AddDays(-reminder.DaysInAdvance) < today)
        {
            // If it has already passed this year, calculate for next year
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
            // If next year is also invalid (e.g. Feb 29), try the year after
            return GetNextYearDate(reminder, year + 1);
        }
    }
}
