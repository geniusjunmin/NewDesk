using System;
using System.Globalization;

public class Program
{
    public static void Run()
    {
        try
        {
            var calendar = new ChineseLunisolarCalendar();
            var date = DateTime.Now;
            // For testing, user's date is 2026-02-06
            // But let's try to find a date that crashes if today doesn't
            // Or just print what today is
            
            int year = calendar.GetYear(date);
            int month = calendar.GetMonth(date);
            int day = calendar.GetDayOfMonth(date);
            int leapMonth = calendar.GetLeapMonth(year);

            Console.WriteLine($"Date: {date}");
            Console.WriteLine($"Year: {year}");
            Console.WriteLine($"Month: {month}");
            Console.WriteLine($"Day: {day}");
            Console.WriteLine($"LeapMonth: {leapMonth}");

            string[] lunarMonthNames = { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
            
            // Replicating the crash logic
            string mName = lunarMonthNames[month - 1];
            Console.WriteLine($"Month Name: {mName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Crashed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
