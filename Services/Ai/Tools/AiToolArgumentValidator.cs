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
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return ToolValidationResult.Success(); // Empty parameters allowed

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

        if (root.TryGetProperty("dueAt", out var dueElem) && dueElem.ValueKind == JsonValueKind.String)
        {
            string dueStr = dueElem.GetString() ?? "";
            if (!string.IsNullOrEmpty(dueStr) && !DateTime.TryParse(dueStr, out _))
            {
                return ToolValidationResult.Fail($"提醒时间 (dueAt) '{dueStr}' 不是有效的 ISO 8601 日期格式。");
            }
        }

        if (root.TryGetProperty("month", out var mElem) && mElem.ValueKind == JsonValueKind.Number)
        {
            int month = mElem.GetInt32();
            if (month < 1 || month > 12)
                return ToolValidationResult.Fail($"提醒月份 (month) '{month}' 超出 1-12 范围。");
        }

        if (root.TryGetProperty("day", out var dElem) && dElem.ValueKind == JsonValueKind.Number)
        {
            int day = dElem.GetInt32();
            if (day < 1 || day > 31)
                return ToolValidationResult.Fail($"提醒日期 (day) '{day}' 超出 1-31 范围。");
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
        // Parameter name is optional for switch_wallpaper ({} is valid)
        if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
        {
            string name = n.GetString() ?? "";
            if (name.Contains("..") || name.Contains('/') || name.Contains('\\'))
                return ToolValidationResult.Fail("壁纸名称包含非法路径字符。");
        }

        return ToolValidationResult.Success();
    }
}
