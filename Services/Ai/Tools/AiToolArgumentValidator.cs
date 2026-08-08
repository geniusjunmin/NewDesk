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
        string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(title))
            return ToolValidationResult.Fail("提醒标题 (title) 不能为空。");

        string scheduleType = root.TryGetProperty("scheduleType", out var st) ? st.GetString() ?? "OneTime" : "OneTime";

        if (scheduleType == "OneTime")
        {
            if (!root.TryGetProperty("dueAt", out var dueElem) || dueElem.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dueElem.GetString()))
            {
                // Fallback check if month and day are supplied
                if (!root.TryGetProperty("month", out _) || !root.TryGetProperty("day", out _))
                {
                    return ToolValidationResult.Fail("一次性提醒 (OneTime) 必须指定到期时间 (dueAt)。");
                }
            }

            if (root.TryGetProperty("dueAt", out var dueVal) && dueVal.ValueKind == JsonValueKind.String)
            {
                string dueStr = dueVal.GetString() ?? "";
                if (!string.IsNullOrEmpty(dueStr) && !DateTime.TryParse(dueStr, out _))
                {
                    return ToolValidationResult.Fail($"到期时间 (dueAt) '{dueStr}' 不是有效的日期格式。");
                }
            }
        }
        else if (scheduleType == "Yearly" || scheduleType == "LunarYearly")
        {
            if (!root.TryGetProperty("month", out var mElem) || mElem.ValueKind != JsonValueKind.Number)
            {
                return ToolValidationResult.Fail("每年提醒 (Yearly / LunarYearly) 必须指定月份 (month)。");
            }
            if (!root.TryGetProperty("day", out var dElem) || dElem.ValueKind != JsonValueKind.Number)
            {
                return ToolValidationResult.Fail("每年提醒 (Yearly / LunarYearly) 必须指定日期 (day)。");
            }

            int month = mElem.GetInt32();
            int day = dElem.GetInt32();

            if (month < 1 || month > 12)
            {
                return ToolValidationResult.Fail($"提醒月份 (month) '{month}' 超出 1-12 范围。");
            }

            int maxDaysInMonth = DateTime.DaysInMonth(2024, month); // Leap year reference
            if (day < 1 || day > maxDaysInMonth)
            {
                return ToolValidationResult.Fail($"提醒日期 (day) '{day}' 对于 {month} 月无效 (最大 {maxDaysInMonth} 天)。");
            }
        }

        if (root.TryGetProperty("daysInAdvance", out var advElem) && advElem.ValueKind == JsonValueKind.Number)
        {
            int advance = advElem.GetInt32();
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
