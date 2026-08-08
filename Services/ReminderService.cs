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
            if (reminder.SnoozeUntil.HasValue && reminder.SnoozeUntil.Value > now) continue;

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

            if (reminder.ScheduleType == ReminderScheduleType.OneTime && reminder.DueAt.HasValue)
            {
                DateTime due = reminder.DueAt.Value;
                DateTime triggerStart = due.AddDays(-reminder.DaysInAdvance);

                if (now >= triggerStart)
                {
                    bool isFinalDue = now >= due;
                    if (Application.Current != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ShowReminderToast(reminder);
                            reminder.LastNotifiedDate = now;
                            if (isFinalDue)
                            {
                                reminder.IsCompleted = true;
                            }
                            DataService.SaveReminders(_reminders);
                        });
                    }
                    else
                    {
                        reminder.LastNotifiedDate = now;
                        if (isFinalDue)
                        {
                            reminder.IsCompleted = true;
                        }
                    }
                }
            }
            else
            {
                UpdateNextReminderDate(reminder);

                if (now.Date >= reminder.NextReminderDate.AddDays(-reminder.DaysInAdvance))
                {
                    if (Application.Current != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ShowReminderToast(reminder);
                            reminder.LastNotifiedDate = now;
                            DataService.SaveReminders(_reminders);
                        });
                    }
                    else
                    {
                        reminder.LastNotifiedDate = now;
                    }
                }
            }
        }
    }

    private static void ShowReminderToast(Reminder reminder)
    {
        string message = reminder.DueAt.HasValue
            ? $"提醒时间: {reminder.DueAt.Value:yyyy-MM-dd HH:mm}"
            : $"今天是 {reminder.NextReminderDate:yyyy-MM-dd}！";

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
