using System;
using System.Globalization;
using NewDesk.Models;

namespace NewDesk.Services;

public sealed class ReminderOccurrenceWindow
{
    public DateTime Occurrence { get; init; }
    public DateTime AdvanceStart { get; init; }
    public DateTime DueGraceEnd { get; init; }
}

public static class ReminderScheduleCalculator
{
    public static DateTime? GetNextOccurrence(Reminder reminder, DateTime now)
    {
        if (reminder == null) return null;

        if (reminder.ScheduleType == ReminderScheduleType.OneTime)
        {
            if (reminder.IsCompleted) return null;
            return reminder.DueAt;
        }

        int hour = reminder.TimeOfDay?.Hours ?? 9;
        int minute = reminder.TimeOfDay?.Minutes ?? 0;

        if (reminder.ScheduleType == ReminderScheduleType.Yearly)
        {
            int year = now.Year;
            int month = Math.Clamp(reminder.Month, 1, 12);
            int day = Math.Clamp(reminder.Day, 1, DateTime.DaysInMonth(year, month));

            var occurrence = new DateTime(year, month, day, hour, minute, 0);

            if (occurrence < now)
            {
                year++;
                day = Math.Clamp(reminder.Day, 1, DateTime.DaysInMonth(year, month));
                occurrence = new DateTime(year, month, day, hour, minute, 0);
            }

            return occurrence;
        }

        if (reminder.ScheduleType == ReminderScheduleType.LunarYearly)
        {
            try
            {
                var calendar = new ChineseLunisolarCalendar();
                int currentLunarYear = calendar.GetYear(now);

                var candidate = GetSolarDateFromLunar(calendar, currentLunarYear, reminder.Month, reminder.Day, hour, minute);
                if (candidate.HasValue && candidate.Value < now)
                {
                    candidate = GetSolarDateFromLunar(calendar, currentLunarYear + 1, reminder.Month, reminder.Day, hour, minute);
                }

                return candidate;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public static ReminderOccurrenceWindow? GetNotificationOccurrence(Reminder reminder, DateTime now)
    {
        if (reminder == null || reminder.IsCompleted)
        {
            return null;
        }

        if (reminder.ScheduleType == ReminderScheduleType.OneTime)
        {
            return reminder.DueAt.HasValue
                ? CreateWindow(reminder.DueAt.Value, reminder.DaysInAdvance)
                : null;
        }

        int hour = reminder.TimeOfDay?.Hours ?? 9;
        int minute = reminder.TimeOfDay?.Minutes ?? 0;
        DateTime? currentOccurrence = null;
        DateTime? nextOccurrence = null;

        if (reminder.ScheduleType == ReminderScheduleType.Yearly)
        {
            currentOccurrence = CreateGregorianOccurrence(reminder, now.Year, hour, minute);
            nextOccurrence = CreateGregorianOccurrence(reminder, now.Year + 1, hour, minute);
        }
        else if (reminder.ScheduleType == ReminderScheduleType.LunarYearly)
        {
            try
            {
                var calendar = new ChineseLunisolarCalendar();
                int currentLunarYear = calendar.GetYear(now);
                currentOccurrence = GetSolarDateFromLunar(calendar, currentLunarYear, reminder.Month, reminder.Day, hour, minute);
                nextOccurrence = GetSolarDateFromLunar(calendar, currentLunarYear + 1, reminder.Month, reminder.Day, hour, minute);
            }
            catch
            {
                return null;
            }
        }

        return FindActiveWindow(currentOccurrence, nextOccurrence, reminder.DaysInAdvance, now);
    }

    private static ReminderOccurrenceWindow? FindActiveWindow(
        DateTime? currentOccurrence,
        DateTime? nextOccurrence,
        int daysInAdvance,
        DateTime now)
    {
        if (currentOccurrence.HasValue)
        {
            var window = CreateWindow(currentOccurrence.Value, daysInAdvance);
            if (now >= window.AdvanceStart && now < window.DueGraceEnd)
            {
                return window;
            }
        }

        if (nextOccurrence.HasValue)
        {
            var window = CreateWindow(nextOccurrence.Value, daysInAdvance);
            if (now >= window.AdvanceStart && now < window.DueGraceEnd)
            {
                return window;
            }
        }

        return null;
    }

    private static ReminderOccurrenceWindow CreateWindow(DateTime occurrence, int daysInAdvance)
    {
        return new ReminderOccurrenceWindow
        {
            Occurrence = occurrence,
            AdvanceStart = occurrence.AddDays(-Math.Max(0, daysInAdvance)),
            DueGraceEnd = occurrence.AddDays(1)
        };
    }

    private static DateTime CreateGregorianOccurrence(Reminder reminder, int year, int hour, int minute)
    {
        int month = Math.Clamp(reminder.Month, 1, 12);
        int day = Math.Clamp(reminder.Day, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, hour, minute, 0);
    }

    private static DateTime? GetSolarDateFromLunar(ChineseLunisolarCalendar calendar, int lunarYear, int lunarMonth, int lunarDay, int hour, int minute)
    {
        try
        {
            int leapMonth = calendar.GetLeapMonth(lunarYear);
            int realMonth = lunarMonth;
            if (leapMonth > 0 && lunarMonth >= leapMonth)
            {
                realMonth++;
            }

            int daysInMonth = calendar.GetDaysInMonth(lunarYear, realMonth);
            int realDay = Math.Clamp(lunarDay, 1, daysInMonth);

            DateTime solarDate = calendar.ToDateTime(lunarYear, realMonth, realDay, hour, minute, 0, 0);
            return solarDate;
        }
        catch
        {
            return null;
        }
    }
}
