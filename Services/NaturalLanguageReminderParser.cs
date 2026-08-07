using System;
using System.Globalization;
using System.Text.RegularExpressions;
using NewDesk.Models;

namespace NewDesk.Services;

public static class NaturalLanguageReminderParser
{
    public static Reminder Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("提醒文本不能为空");
        }

        string text = input.Trim();
        DateTime targetDate = DateTime.Now.AddDays(1); // Default to tomorrow
        bool isLunar = text.Contains("农历") || text.Contains("阴历");
        string title = text;

        // 1. Check for "明天" / "后天" / "大后天" / "今天"
        if (Regex.IsMatch(text, @"今天"))
        {
            targetDate = DateTime.Now;
            title = Regex.Replace(title, @"今天", "").Trim();
        }
        else if (Regex.IsMatch(text, @"明天"))
        {
            targetDate = DateTime.Now.AddDays(1);
            title = Regex.Replace(title, @"明天", "").Trim();
        }
        else if (Regex.IsMatch(text, @"后天"))
        {
            targetDate = DateTime.Now.AddDays(2);
            title = Regex.Replace(title, @"后天", "").Trim();
        }
        else if (Regex.IsMatch(text, @"大后天"))
        {
            targetDate = DateTime.Now.AddDays(3);
            title = Regex.Replace(title, @"大后天", "").Trim();
        }

        // 2. Check for "M月D日" or "M-D" or "M/D"
        var monthDayMatch = Regex.Match(text, @"(\d{1,2})\s*月\s*(\d{1,2})\s*日?");
        if (monthDayMatch.Success && int.TryParse(monthDayMatch.Groups[1].Value, out int m) && int.TryParse(monthDayMatch.Groups[2].Value, out int d))
        {
            if (m >= 1 && m <= 12 && d >= 1 && d <= 31)
            {
                targetDate = new DateTime(DateTime.Now.Year, m, d);
                if (targetDate < DateTime.Today) targetDate = targetDate.AddYears(1);
                title = Regex.Replace(title, @"\d{1,2}\s*月\s*\d{1,2}\s*日?", "").Trim();
            }
        }

        // Clean up title
        title = Regex.Replace(title, @"^(提醒我|记得|需要|去|帮我|提醒)\s*", "").Trim();
        if (string.IsNullOrEmpty(title)) title = input;

        return new Reminder
        {
            Title = title,
            Month = targetDate.Month,
            Day = targetDate.Day,
            IsLunar = isLunar,
            DaysInAdvance = 1,
            Category = "工作"
        };
    }
}
