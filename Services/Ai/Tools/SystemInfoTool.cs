using System;
using System.Text.Json;
using System.Threading.Tasks;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai.Tools;

public class SystemInfoTool : IAiTool
{
    public string Name => "get_system_info";
    public string Description => "获取当前 Windows 系统版本、操作系统环境与 NewDesk 客户端版本信息。只读工具，无需确认。";
    public bool RequiresUserConfirmation => false;

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { }
    };

    public string BuildConfirmationPreview(string argumentsJson)
    {
        return "🤖 AI 准备读取当前 Windows 系统与客户端版本信息（只读）。";
    }

    public Task<AiToolResult> ExecuteAsync(string argumentsJson)
    {
        var info = new
        {
            OSVersion = Environment.OSVersion.ToString(),
            Is64BitOS = Environment.Is64BitOperatingSystem,
            ProcessorCount = Environment.ProcessorCount,
            MachineName = Environment.MachineName,
            AppVersion = "2.2.0",
            DotNetVersion = Environment.Version.ToString()
        };

        return Task.FromResult(new AiToolResult
        {
            OutputJson = JsonSerializer.Serialize(info)
        });
    }
}
