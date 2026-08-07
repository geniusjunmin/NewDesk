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

            await Task.Yield();
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
