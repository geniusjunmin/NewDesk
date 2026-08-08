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
            scheduleType = new { type = "string", enumValues = new[] { "OneTime", "Yearly", "LunarYearly" }, description = "提醒类型：OneTime (一次性), Yearly (公历每年), LunarYearly (农历每年)" },
            dueAt = new { type = "string", description = "提醒到期时间 (一次性提醒必需)，格式为 ISO 8601 (例: 2026-08-09T15:00:00)" },
            month = new { type = "integer", description = "公历或农历月份 (1-12，每年提醒必需)" },
            day = new { type = "integer", description = "公历或农历日期 (1-31，每年提醒必需)" },
            time = new { type = "string", description = "每天提醒的具体时间 (例: 09:00)" },
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
            string scheduleType = root.TryGetProperty("scheduleType", out var st) ? st.GetString() ?? "OneTime" : "OneTime";

            string dueStr = "未指定";
            if (scheduleType == "OneTime")
            {
                if (root.TryGetProperty("dueAt", out var dueElem) && dueElem.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(dueElem.GetString(), out var d))
                    {
                        dueStr = d.ToString("yyyy-MM-dd HH:mm");
                    }
                }
            }
            else if (root.TryGetProperty("month", out var m) && root.TryGetProperty("day", out var d))
            {
                string typeLabel = scheduleType == "LunarYearly" ? "每年农历" : "每年公历";
                dueStr = $"{typeLabel} {m.GetInt32()}月{d.GetInt32()}日";
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
            string scheduleTypeStr = root.TryGetProperty("scheduleType", out var stElem) ? stElem.GetString() ?? "OneTime" : "OneTime";
            ReminderScheduleType scheduleType = scheduleTypeStr switch
            {
                "Yearly" => ReminderScheduleType.Yearly,
                "LunarYearly" => ReminderScheduleType.LunarYearly,
                _ => ReminderScheduleType.OneTime
            };

            DateTime? dueAt = null;
            TimeSpan timeOfDay = new TimeSpan(9, 0, 0);
            int month = DateTime.Now.Month;
            int day = DateTime.Now.Day;

            if (root.TryGetProperty("time", out var timeElem) && timeElem.ValueKind == JsonValueKind.String)
            {
                if (TimeSpan.TryParse(timeElem.GetString(), out var parsedTime))
                {
                    timeOfDay = parsedTime;
                }
            }

            if (scheduleType == ReminderScheduleType.OneTime)
            {
                if (root.TryGetProperty("dueAt", out var dueElem) && dueElem.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(dueElem.GetString(), out var parsedDue))
                    {
                        dueAt = parsedDue;
                        month = parsedDue.Month;
                        day = parsedDue.Day;
                        timeOfDay = parsedDue.TimeOfDay;
                    }
                }
                else if (root.TryGetProperty("month", out var mElem) && root.TryGetProperty("day", out var dElem))
                {
                    month = mElem.GetInt32();
                    day = dElem.GetInt32();
                    dueAt = new DateTime(DateTime.Now.Year, month, day, timeOfDay.Hours, timeOfDay.Minutes, 0);
                }
            }
            else
            {
                if (root.TryGetProperty("month", out var mElem)) month = mElem.GetInt32();
                if (root.TryGetProperty("day", out var dElem)) day = dElem.GetInt32();
            }

            int advance = root.TryGetProperty("daysInAdvance", out var advElem) ? advElem.GetInt32() : 1;
            string notes = root.TryGetProperty("notes", out var nElem) ? nElem.GetString() ?? "" : "";

            var reminders = DataService.LoadReminders();
            var newReminder = new Reminder
            {
                Title = title,
                Month = month,
                Day = day,
                DueAt = dueAt,
                TimeOfDay = timeOfDay,
                DaysInAdvance = advance,
                Notes = notes,
                ScheduleType = scheduleType,
                IsLunar = scheduleType == ReminderScheduleType.LunarYearly
            };
            reminders.Add(newReminder);
            DataService.SaveReminders(reminders);

            string dateInfo = dueAt.HasValue ? dueAt.Value.ToString("yyyy-MM-dd HH:mm") : $"{month}月{day}日";
            return Task.FromResult(new AiToolResult
            {
                OutputJson = JsonSerializer.Serialize(new { success = true, message = $"已成功为您创建提醒：{title} ({dateInfo})" })
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
