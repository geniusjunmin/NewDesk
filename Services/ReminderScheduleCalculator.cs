using System;
using System.Globalization;
using NewDesk.Models;

namespace NewDesk.Services;

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
                int currentSolarYear = now.Year;

                var candidate = GetSolarDateFromLunar(calendar, currentSolarYear, reminder.Month, reminder.Day, hour, minute);
                if (candidate.HasValue && candidate.Value < now)
                {
                    candidate = GetSolarDateFromLunar(calendar, currentSolarYear + 1, reminder.Month, reminder.Day, hour, minute);
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
