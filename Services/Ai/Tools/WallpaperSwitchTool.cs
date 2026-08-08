using System;
using System.Text.Json;
using System.Threading.Tasks;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai.Tools;

public class WallpaperSwitchTool : IAiTool
{
    public string Name => "switch_wallpaper";
    public string Description => "自动轮播或切换至下一张桌面壁纸。需要用户确认。";
    public bool RequiresUserConfirmation => true;

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { }
    };

    public string BuildConfirmationPreview(string argumentsJson)
    {
        return "🤖 AI 准备为您切换桌面壁纸至下一张模板。";
    }

    public async Task<AiToolResult> ExecuteAsync(string argumentsJson)
    {
        try
        {
            await WallpaperService.RotateNextAsync();
            return new AiToolResult
            {
                OutputJson = JsonSerializer.Serialize(new { success = true, message = "桌面壁纸已成功切换！" })
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
