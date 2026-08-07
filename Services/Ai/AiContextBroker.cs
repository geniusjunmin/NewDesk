using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NewDesk.Models;

namespace NewDesk.Services.Ai;

public static class AiContextBroker
{
    public static string BuildContextPrompt()
    {
        var settings = SettingsService.LoadSettings();
        var sb = new StringBuilder();

        sb.AppendLine($"[当前系统时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss dddd}]");

        // 1. Reminders Context
        if (settings.AllowAiReminderContext)
        {
            try
            {
                var reminders = DataService.LoadReminders().Where(r => !r.IsCompleted).Take(5).ToList();
                if (reminders.Count > 0)
                {
                    sb.AppendLine("[当前待办日程提醒]:");
                    foreach (var r in reminders)
                    {
                        sb.AppendLine($" - {r.Title} ({r.Month}月{r.Day}日)");
                    }
                }
            }
            catch { }
        }

        // 2. Wallpaper Context
        if (settings.AllowAiWallpaperContext)
        {
            sb.AppendLine($"[当前桌面壁纸: {settings.CurrentWallpaperName ?? "默认壁纸"}]");
        }

        return sb.ToString();
    }
}
