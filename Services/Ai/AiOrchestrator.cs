using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewDesk.Models.Ai;
using NewDesk.Services.Ai.Tools;

namespace NewDesk.Services.Ai;

public class AiTurnRequest
{
    public AiTaskProfile TaskProfile { get; set; } = AiTaskProfile.GeneralChat;
    public string UserPrompt { get; set; } = string.Empty;
    public List<AiMessage> ConversationHistory { get; set; } = new();
    public AiProviderConfig? PreferredProvider { get; set; }
    public DataSensitivity DataSensitivity { get; set; } = DataSensitivity.Personal;
    public Func<PendingToolAction, Task<bool>>? ConfirmationCallback { get; set; }
}

public enum AiTaskProfile
{
    GeneralChat,
    FastCommand,
    Reasoning,
    Vision,
    PrivateLocal,
    WallpaperGeneration,
    LogAnalysis
}

public static class AiOrchestrator
{
    private const int MaxToolRounds = 4;

    public static async Task<AiResponse> ExecuteTurnAsync(
        AiTurnRequest turnRequest,
        IProgress<AiStreamChunk>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Select Provider
        var providerConfig = turnRequest.PreferredProvider ?? AiProviderRegistry.GetDefaultProvider();
        if (providerConfig == null)
        {
            throw new InvalidOperationException("未找到可用的 AI 服务提供商，请先在【系统设置 -> AI 服务】中配置。");
        }

        // 2. Privacy Guard Validation
        string sanitizedPrompt = SecretRedactor.Redact(turnRequest.UserPrompt);
        AiPrivacyGuard.ValidateRequest(providerConfig, turnRequest.DataSensitivity, sanitizedPrompt);

        // 3. Inject Context
        string contextPrompt = AiContextBroker.BuildContextPrompt();
        string systemPrompt = string.IsNullOrEmpty(contextPrompt)
            ? "你是一个专业、高效的 Windows 桌面 AI 助手。"
            : $"你是一个专业、高效的 Windows 桌面 AI 助手。\n\n{contextPrompt}";

        var provider = AiProviderFactory.CreateProvider(providerConfig);

        // 4. Build Outbound Messages Payload with Redaction
        var outboundMessages = AiOutboundContextBuilder.BuildMessages(turnRequest.ConversationHistory, turnRequest.UserPrompt);

        var toolDefs = provider.Capabilities.SupportsTools ? AiToolRegistry.GetToolDefinitions() : null;

        int currentRound = 0;
        AiResponse finalResponse = new();

        while (currentRound < MaxToolRounds)
        {
            currentRound++;

            var aiRequest = new AiRequest
            {
                Model = providerConfig.SelectedModel,
                Messages = outboundMessages,
                SystemPrompt = systemPrompt,
                Stream = providerConfig.Streaming && progress != null && currentRound == 1,
                Tools = toolDefs
            };

            AiResponse roundResponse;
            if (aiRequest.Stream && progress != null)
            {
                roundResponse = new AiResponse();
                await foreach (var chunk in provider.StreamAsync(aiRequest, cancellationToken))
                {
                    if (!string.IsNullOrEmpty(chunk.TextDelta))
                    {
                        roundResponse.Content += chunk.TextDelta;
                    }
                    if (chunk.ToolCalls != null && chunk.ToolCalls.Count > 0)
                    {
                        roundResponse.ToolCalls.AddRange(chunk.ToolCalls);
                    }
                    progress.Report(chunk);
                }
            }
            else
            {
                roundResponse = await provider.CompleteAsync(aiRequest, cancellationToken);
            }

            finalResponse = roundResponse;

            // Check if model requested tool execution
            if (roundResponse.ToolCalls == null || roundResponse.ToolCalls.Count == 0)
            {
                return finalResponse;
            }

            // Execute requested tools and append to conversation history for next round
            outboundMessages.Add(new AiMessage
            {
                Role = "assistant",
                Content = roundResponse.Content,
                ToolCalls = roundResponse.ToolCalls
            });

            foreach (var toolCall in roundResponse.ToolCalls)
            {
                var toolResult = await AiToolExecutionService.ExecuteToolWithPermissionAsync(toolCall, turnRequest.ConfirmationCallback);
                outboundMessages.Add(new AiMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = toolResult.OutputJson
                });
            }
        }

        AppDataPath.LogInfo($"AiOrchestrator Tool Loop reached maximum safe rounds limit ({MaxToolRounds}).");
        finalResponse.Content += "\n[系统提示: AI 工具调用轮次已达到安全限制]";
        return finalResponse;
    }
}
