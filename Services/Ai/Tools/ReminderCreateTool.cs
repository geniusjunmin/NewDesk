using System;
using System.Text.Json;
using System.Threading.Tasks;
using NewDesk.Models;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai.Tools;

public class ReminderCreateTool : IAiTool
{
    public string Name => "create_reminder";
    public string Description => "在 NewDesk 中创建一个新的日程提醒事项。创建前需要用户确认。";
    public bool RequiresUserConfirmation => true;

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "提醒事项标题" },
            month = new { type = "integer", description = "公历月份 (1-12)" },
            day = new { type = "integer", description = "公历日期 (1-31)" },
            daysInAdvance = new { type = "integer", description = "提前几天提醒 (默认 1 天)" }
        },
        required = new[] { "title", "month", "day" }
    };

    public string BuildConfirmationPreview(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "待办事项" : "待办事项";
            int month = root.TryGetProperty("month", out var m) ? m.GetInt32() : DateTime.Now.Month;
            int day = root.TryGetProperty("day", out var d) ? d.GetInt32() : DateTime.Now.Day;
            int advance = root.TryGetProperty("daysInAdvance", out var a) ? a.GetInt32() : 1;

            return $"🤖 AI 准备为您创建以下提醒事项：\n\n• 事项标题：{title}\n• 提醒时间：{month}月{day}日\n• 提前通知：提前 {advance} 天";
        }
        catch
        {
            return $"🤖 AI 准备为您创建提醒事项。\n参数: {argumentsJson}";
        }
    }

    public async Task<AiToolResult> ExecuteAsync(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            string title = root.GetProperty("title").GetString() ?? "新提醒";
            int month = root.GetProperty("month").GetInt32();
            int day = root.GetProperty("day").GetInt32();
            int advance = root.TryGetProperty("daysInAdvance", out var advElem) ? advElem.GetInt32() : 1;

            var reminders = DataService.LoadReminders();
            var newReminder = new Reminder
            {
                Title = title,
                Month = month,
                Day = day,
                DaysInAdvance = advance
            };
            reminders.Add(newReminder);
            DataService.SaveReminders(reminders);

            return new AiToolResult
            {
                OutputJson = JsonSerializer.Serialize(new { success = true, message = $"已成功为您创建提醒：{title} ({month}月{day}日)" })
            };
        }
        catch (Exception ex)
        {
            return new AiToolResult
            {
                IsError = true,
                OutputJson = JsonSerializer.Serialize(new { success = false, error = ex.Message })
            };
        }
    }
}
