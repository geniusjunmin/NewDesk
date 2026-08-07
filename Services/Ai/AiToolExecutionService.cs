using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewDesk.Models.Ai;
using NewDesk.Services.Ai.Tools;

namespace NewDesk.Services.Ai;

public enum AiToolPermission
{
    ReadOnly,
    RequiresConfirmation,
    Forbidden
}

public class PendingToolAction
{
    public IAiTool Tool { get; set; } = null!;
    public string ArgumentsJson { get; set; } = "{}";
    public string HumanReadablePreview { get; set; } = string.Empty;
}

public static class AiToolExecutionService
{
    private static readonly HashSet<string> ForbiddenToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "shell", "powershell", "cmd", "execute_command", "read_password", "modify_password", "get_master_password"
    };

    public static AiToolPermission GetToolPermission(string toolName)
    {
        if (ForbiddenToolNames.Contains(toolName))
        {
            return AiToolPermission.Forbidden;
        }

        var tool = AiToolRegistry.GetTool(toolName);
        if (tool == null) return AiToolPermission.Forbidden;

        return tool.RequiresUserConfirmation ? AiToolPermission.RequiresConfirmation : AiToolPermission.ReadOnly;
    }

    public static async Task<AiToolResult> ExecuteToolWithPermissionAsync(
        AiToolCall toolCall,
        Func<PendingToolAction, Task<bool>>? userConfirmationCallback = null)
    {
        var permission = GetToolPermission(toolCall.Name);

        if (permission == AiToolPermission.Forbidden)
        {
            return new AiToolResult
            {
                ToolCallId = toolCall.Id,
                IsError = true,
                OutputJson = "{\"error\": \"安全防线系统阻断：所请求的工具违反安全防线策略，已被禁止执行！\"}"
            };
        }

        var tool = AiToolRegistry.GetTool(toolCall.Name);
        if (tool == null)
        {
            return new AiToolResult
            {
                ToolCallId = toolCall.Id,
                IsError = true,
                OutputJson = "{\"error\": \"未找到指定的 AI 工具。\"}"
            };
        }

        if (permission == AiToolPermission.RequiresConfirmation && userConfirmationCallback != null)
        {
            var pendingAction = new PendingToolAction
            {
                Tool = tool,
                ArgumentsJson = toolCall.ArgumentsJson,
                HumanReadablePreview = $"🤖 AI 请求执行操作 [{tool.Name}]: {tool.Description}\n参数: {toolCall.ArgumentsJson}"
            };

            bool confirmed = await userConfirmationCallback(pendingAction);
            if (!confirmed)
            {
                return new AiToolResult
                {
                    ToolCallId = toolCall.Id,
                    IsError = true,
                    OutputJson = "{\"error\": \"用户拒绝了该操作的执行。\"}"
                };
            }
        }

        var result = await tool.ExecuteAsync(toolCall.ArgumentsJson);
        result.ToolCallId = toolCall.Id;
        return result;
    }
}
