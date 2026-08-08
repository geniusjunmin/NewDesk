using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NewDesk.Models;

namespace NewDesk.Services.Ai;

public class AiContextResult
{
    public string Prompt { get; set; } = string.Empty;
    public AiDataCategory IncludedCategories { get; set; } = AiDataCategory.None;
}

public static class AiContextBroker
{
    public static AiContextResult BuildContextPrompt(AiDataCategory requestedCategories = AiDataCategory.None)
    {
        var settings = SettingsService.LoadSettings();
        var sb = new StringBuilder();
        var included = AiDataCategory.None;

        sb.AppendLine($"[当前系统时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss dddd}]");

        // 1. Reminders Context
        if (settings.AllowAiReminderContext && requestedCategories.HasFlag(AiDataCategory.Reminder))
        {
            try
            {
                var reminders = DataService.LoadReminders().Where(r => !r.IsCompleted).Take(5).ToList();
                if (reminders.Count > 0)
                {
                    sb.AppendLine("[当前待办日程提醒]:");
                    foreach (var r in reminders)
                    {
                        string timeStr = r.DueAt.HasValue ? r.DueAt.Value.ToString("yyyy-MM-dd HH:mm") : $"{r.Month}月{r.Day}日";
                        sb.AppendLine($" - {r.Title} ({timeStr})");
                    }
                    included |= AiDataCategory.Reminder;
                }
            }
            catch { }
        }

        // 2. Wallpaper Context
        if (settings.AllowAiWallpaperContext && requestedCategories.HasFlag(AiDataCategory.Wallpaper))
        {
            sb.AppendLine($"[当前桌面壁纸: {settings.CurrentWallpaperName ?? "默认壁纸"}]");
            included |= AiDataCategory.Wallpaper;
        }

        // 3. Password Metadata Context (NO PASSWORDS)
        if (settings.AllowAiPasswordMetadata && requestedCategories.HasFlag(AiDataCategory.PasswordMetadata))
        {
            try
            {
                var passwords = DataService.LoadPasswords().Take(10).ToList();
                if (passwords.Count > 0)
                {
                    sb.AppendLine("[密码库站点元数据 (无密码)]:");
                    foreach (var p in passwords)
                    {
                        sb.AppendLine($" - {p.Title} | 用户名: {p.Username} | 网址: {p.WebsiteUrl}");
                    }
                    included |= AiDataCategory.PasswordMetadata;
                }
            }
            catch { }
        }

        return new AiContextResult
        {
            Prompt = sb.ToString(),
            IncludedCategories = included
        };
    }
}
