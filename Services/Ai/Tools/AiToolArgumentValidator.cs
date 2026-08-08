using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NewDesk.Services.Ai.Tools;

public class ToolValidationResult
{
    public bool IsValid { get; set; } = true;
    public string ErrorMessage { get; set; } = string.Empty;

    public static ToolValidationResult Success() => new ToolValidationResult { IsValid = true };
    public static ToolValidationResult Fail(string message) => new ToolValidationResult { IsValid = false, ErrorMessage = message };
}

public static class AiToolArgumentValidator
{
    public static ToolValidationResult ValidateArguments(string toolName, string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson) || argumentsJson.Trim() == "{}")
        {
            if (toolName == "switch_wallpaper")
            {
                return ToolValidationResult.Success();
            }
            return ToolValidationResult.Fail($"工具 '{toolName}' 要求必需的参数，参数不能为空。");
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            switch (toolName)
            {
                case "create_reminder":
                    return ValidateReminderArguments(root);

                case "switch_wallpaper":
                    return ValidateWallpaperArguments(root);

                default:
                    return ToolValidationResult.Success();
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Fail($"工具参数不是有效的 JSON 格式: {ex.Message}");
        }
    }

    private static ToolValidationResult ValidateReminderArguments(JsonElement root)
    {
        string title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(title))
            return ToolValidationResult.Fail("提醒标题 (title) 不能为空。");

        if (!root.TryGetProperty("scheduleType", out var st) || st.ValueKind != JsonValueKind.String)
            return ToolValidationResult.Fail("必须指定提醒类型 (scheduleType)。");

        string scheduleType = st.GetString() ?? "";
        if (scheduleType is not ("OneTime" or "Yearly" or "LunarYearly"))
            return ToolValidationResult.Fail($"未知提醒类型 (scheduleType): '{scheduleType}'。");

        if (scheduleType == "OneTime")
        {
            if (!root.TryGetProperty("dueAt", out var dueElem) || dueElem.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dueElem.GetString()))
                return ToolValidationResult.Fail("一次性提醒 (OneTime) 必须指定到期时间 (dueAt)。");

            string dueStr = dueElem.GetString()!;
            if (!DateTime.TryParse(dueStr, out var dueAt))
                return ToolValidationResult.Fail($"到期时间 (dueAt) '{dueStr}' 不是有效的日期格式。");
            if (dueAt <= DateTime.Now)
                return ToolValidationResult.Fail("一次性提醒的到期时间 (dueAt) 必须晚于当前时间。");
        }
        else
        {
            if (!root.TryGetProperty("month", out var mElem) || !mElem.TryGetInt32(out int month))
                return ToolValidationResult.Fail("每年提醒必须指定整数月份 (month)。");
            if (!root.TryGetProperty("day", out var dElem) || !dElem.TryGetInt32(out int day))
                return ToolValidationResult.Fail("每年提醒必须指定整数日期 (day)。");

            if (month < 1 || month > 12)
            {
                return ToolValidationResult.Fail($"提醒月份 (month) '{month}' 超出 1-12 范围。");
            }

            int maxDay = scheduleType == "LunarYearly" ? 30 : DateTime.DaysInMonth(2024, month);
            if (day < 1 || day > maxDay)
            {
                return ToolValidationResult.Fail($"提醒日期 (day) '{day}' 对于该类型无效（最大 {maxDay} 天）。");
            }
        }

        if (root.TryGetProperty("time", out var timeElem) &&
            (timeElem.ValueKind != JsonValueKind.String ||
             !ReminderTimeParser.TryParseClockTime(timeElem.GetString(), out _)))
        {
            return ToolValidationResult.Fail("提醒时间 (time) 必须是 H:mm 或 HH:mm，且位于 00:00-23:59。");
        }

        if (root.TryGetProperty("daysInAdvance", out var advElem))
        {
            if (!advElem.TryGetInt32(out int advance))
                return ToolValidationResult.Fail("提前提醒天数 (daysInAdvance) 必须是整数。");
            if (advance < 0)
                return ToolValidationResult.Fail("提前提醒天数 (daysInAdvance) 不能为负数。");
        }

        return ToolValidationResult.Success();
    }

    private static ToolValidationResult ValidateWallpaperArguments(JsonElement root)
    {
        if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
        {
            string name = n.GetString() ?? "";
            if (name.Contains("..") || name.Contains('/') || name.Contains('\\'))
                return ToolValidationResult.Fail("壁纸名称包含非法路径字符。");
        }

        return ToolValidationResult.Success();
    }
}
