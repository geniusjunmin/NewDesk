using System.Text.Json;

namespace NewDesk.Models.Ai;

public class ToolExecutionDisplayInfo
{
    public string Icon { get; set; } = "🛠️";
    public string Title { get; set; } = "已执行 AI 工具";
    public string Detail { get; set; } = string.Empty;
    public bool IsSuccess { get; set; } = true;

    public static ToolExecutionDisplayInfo Format(string toolName, string argumentsJson, bool isSuccess)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            switch (toolName)
            {
                case "create_reminder":
                    string remTitle = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    string dueAt = root.TryGetProperty("dueAt", out var d) ? d.GetString() ?? "" : "";
                    return new ToolExecutionDisplayInfo
                    {
                        Icon = "🔔",
                        Title = isSuccess ? "已创建提醒" : "创建提醒失败",
                        Detail = $"{remTitle} · {dueAt}",
                        IsSuccess = isSuccess
                    };

                case "switch_wallpaper":
                    string wpName = root.TryGetProperty("name", out var w) ? w.GetString() ?? "" : "";
                    return new ToolExecutionDisplayInfo
                    {
                        Icon = "🖼️",
                        Title = isSuccess ? "已切换壁纸" : "切换壁纸失败",
                        Detail = string.IsNullOrEmpty(wpName) ? "已切换至下一张壁纸" : wpName,
                        IsSuccess = isSuccess
                    };

                default:
                    return new ToolExecutionDisplayInfo
                    {
                        Icon = "⚙️",
                        Title = isSuccess ? $"工具 {toolName} 已执行" : $"工具 {toolName} 执行失败",
                        Detail = argumentsJson,
                        IsSuccess = isSuccess
                    };
            }
        }
        catch
        {
            return new ToolExecutionDisplayInfo
            {
                Icon = "⚙️",
                Title = toolName,
                Detail = argumentsJson,
                IsSuccess = isSuccess
            };
        }
    }
}
