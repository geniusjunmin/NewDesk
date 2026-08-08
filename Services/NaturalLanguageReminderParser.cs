using System;
using System.Globalization;
using System.Text.RegularExpressions;
using NewDesk.Models;

namespace NewDesk.Services;

public static class NaturalLanguageReminderParser
{
    public static Reminder Parse(string input, IClock? clock = null)
    {
        clock ??= SystemClock.Instance;

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("提醒文本不能为空");
        }

        string text = input.Trim();
        DateTime now = clock.Now;
        DateTime targetDate = now.Date.AddDays(1); // Default to tomorrow
        TimeSpan targetTime = new TimeSpan(9, 0, 0); // Default to 09:00 AM
        bool isLunar = text.Contains("农历") || text.Contains("阴历");
        string title = text;

        // 1. Check for "今天" / "明天" / "后天" / "大后天"
        if (Regex.IsMatch(title, @"今天"))
        {
            targetDate = now.Date;
            title = Regex.Replace(title, @"今天", "").Trim();
        }
        else if (Regex.IsMatch(title, @"明天"))
        {
            targetDate = now.Date.AddDays(1);
            title = Regex.Replace(title, @"明天", "").Trim();
        }
        else if (Regex.IsMatch(title, @"后天"))
        {
            targetDate = now.Date.AddDays(2);
            title = Regex.Replace(title, @"后天", "").Trim();
        }
        else if (Regex.IsMatch(title, @"大后天"))
        {
            targetDate = now.Date.AddDays(3);
            title = Regex.Replace(title, @"大后天", "").Trim();
        }

        // 2. Check for "M月D日" or "M-D"
        var monthDayMatch = Regex.Match(title, @"(\d{1,2})\s*月\s*(\d{1,2})\s*日?");
        if (monthDayMatch.Success && int.TryParse(monthDayMatch.Groups[1].Value, out int m) && int.TryParse(monthDayMatch.Groups[2].Value, out int d))
        {
            if (m >= 1 && m <= 12 && d >= 1 && d <= 31)
            {
                targetDate = new DateTime(now.Year, m, d);
                if (targetDate < now.Date) targetDate = targetDate.AddYears(1);
                title = Regex.Replace(title, @"\d{1,2}\s*月\s*\d{1,2}\s*日?", "").Trim();
            }
        }

        // 3. Check for Time of Day ("下午3点", "下午15:30", "上午9点", "15:00", "8:30")
        var timeMatch = Regex.Match(title, @"(上午|下午|早上|晚上|中午)?\s*(\d{1,2})[:：点](\d{1,2})?\s*(分|点)?");
        if (timeMatch.Success)
        {
            string period = timeMatch.Groups[1].Value;
            if (int.TryParse(timeMatch.Groups[2].Value, out int hour))
            {
                int minute = 0;
                if (timeMatch.Groups[3].Success && int.TryParse(timeMatch.Groups[3].Value, out int parsedMin))
                {
                    minute = parsedMin;
                }

                if (period == "下午" || period == "晚上")
                {
                    if (hour < 12) hour += 12;
                }
                else if (period == "上午" || period == "早上")
                {
                    if (hour == 12) hour = 0;
                }

                if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                {
                    targetTime = new TimeSpan(hour, minute, 0);
                    title = title.Replace(timeMatch.Value, "").Trim();
                }
            }
        }

        // Clean up action prefixes
        title = Regex.Replace(title, @"^(提醒我|记得|需要|去|帮我|提醒)\s*", "").Trim();
        if (string.IsNullOrEmpty(title)) title = input;

        DateTime fullDueAt = targetDate.Date.Add(targetTime);

        return new Reminder
        {
            Title = title,
            Month = fullDueAt.Month,
            Day = fullDueAt.Day,
            DueAt = fullDueAt,
            TimeOfDay = targetTime,
            ScheduleType = isLunar ? ReminderScheduleType.LunarYearly : ReminderScheduleType.OneTime,
            IsLunar = isLunar,
            DaysInAdvance = 1,
            Category = "工作"
        };
    }
}
