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
            dueAt = new { type = "string", description = "提醒到期时间，格式为 ISO 8601 (例: 2026-08-09T15:00:00)" },
            month = new { type = "integer", description = "公历月份 (1-12，可选)" },
            day = new { type = "integer", description = "公历日期 (1-31，可选)" },
            daysInAdvance = new { type = "integer", description = "提前几天提醒 (默认 1 天)" },
            notes = new { type = "string", description = "备注说明 (可选)" }
        },
        required = new[] { "title" }
    };

    public string BuildConfirmationPreview(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "待办事项" : "待办事项";

            string dueStr = "未指定";
            if (root.TryGetProperty("dueAt", out var dueElem) && dueElem.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(dueElem.GetString(), out var d))
                {
                    dueStr = d.ToString("yyyy-MM-dd HH:mm");
                }
            }
            else if (root.TryGetProperty("month", out var m) && root.TryGetProperty("day", out var d))
            {
                dueStr = $"{DateTime.Now.Year}-{m.GetInt32():D2}-{d.GetInt32():D2}";
            }

            int advance = root.TryGetProperty("daysInAdvance", out var a) ? a.GetInt32() : 1;

            return $"🤖 AI 准备为您创建以下提醒事项：\n\n• 事项标题：{title}\n• 提醒时间：{dueStr}\n• 提前通知：提前 {advance} 天";
        }
        catch
        {
            return $"🤖 AI 准备为您创建提醒事项。\n参数: {argumentsJson}";
        }
    }

    public Task<AiToolResult> ExecuteAsync(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            string title = root.GetProperty("title").GetString() ?? "新提醒";

            DateTime dueAt = DateTime.Now.Date.AddDays(1).AddHours(9);
            if (root.TryGetProperty("dueAt", out var dueElem) && dueElem.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(dueElem.GetString(), out var parsedDue))
                {
                    dueAt = parsedDue;
                }
            }
            else if (root.TryGetProperty("month", out var mElem) && root.TryGetProperty("day", out var dElem))
            {
                dueAt = new DateTime(DateTime.Now.Year, mElem.GetInt32(), dElem.GetInt32(), 9, 0, 0);
            }

            int advance = root.TryGetProperty("daysInAdvance", out var advElem) ? advElem.GetInt32() : 1;
            string notes = root.TryGetProperty("notes", out var nElem) ? nElem.GetString() ?? "" : "";

            var reminders = DataService.LoadReminders();
            var newReminder = new Reminder
            {
                Title = title,
                Month = dueAt.Month,
                Day = dueAt.Day,
                DueAt = dueAt,
                TimeOfDay = dueAt.TimeOfDay,
                DaysInAdvance = advance,
                Notes = notes,
                ScheduleType = ReminderScheduleType.OneTime
            };
            reminders.Add(newReminder);
            DataService.SaveReminders(reminders);

            return Task.FromResult(new AiToolResult
            {
                OutputJson = JsonSerializer.Serialize(new { success = true, message = $"已成功为您创建提醒：{title} ({dueAt:yyyy-MM-dd HH:mm})" })
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AiToolResult
            {
                IsError = true,
                OutputJson = JsonSerializer.Serialize(new { success = false, error = ex.Message })
            });
        }
    }
}
