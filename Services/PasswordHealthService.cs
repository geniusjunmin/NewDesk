using System;
using System.Collections.Generic;
using System.Linq;
using NewDesk.Models;

namespace NewDesk.Services;

public class PasswordHealthReport
{
    public int Score { get; set; } = 100;
    public int WeakCount { get; set; }
    public int DuplicateCount { get; set; }
    public int TotalCount { get; set; }
    public List<PasswordEntry> WeakEntries { get; set; } = new();
    public List<PasswordEntry> DuplicateEntries { get; set; } = new();
}

public static class PasswordHealthService
{
    public static PasswordHealthReport Evaluate(List<PasswordEntry> entries)
    {
        var report = new PasswordHealthReport { TotalCount = entries.Count };
        if (entries.Count == 0) return report;

        var pwdCounts = new Dictionary<string, int>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Password)) continue;
            pwdCounts[entry.Password] = pwdCounts.GetValueOrDefault(entry.Password, 0) + 1;
        }

        int totalPenalty = 0;

        foreach (var entry in entries)
        {
            bool isWeak = IsPasswordWeak(entry.Password);
            if (isWeak)
            {
                report.WeakEntries.Add(entry);
                totalPenalty += 15;
            }

            if (!string.IsNullOrEmpty(entry.Password) && pwdCounts.TryGetValue(entry.Password, out int cnt) && cnt > 1)
            {
                report.DuplicateEntries.Add(entry);
                totalPenalty += 10;
            }
        }

        report.WeakCount = report.WeakEntries.Count;
        report.DuplicateCount = report.DuplicateEntries.DistinctBy(e => e.Password).Count();

        report.Score = Math.Max(0, 100 - totalPenalty);
        return report;
    }

    public static bool IsPasswordWeak(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8) return true;

        bool hasDigit = password.Any(char.IsDigit);
        bool hasLetter = password.Any(char.IsLetter);

        return !hasDigit || !hasLetter;
    }
}
