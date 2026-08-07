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

        var messages = new List<AiMessage>(turnRequest.ConversationHistory);
        if (messages.Count == 0 || messages[^1].Role != "user")
        {
            messages.Add(new AiMessage { Role = "user", Content = sanitizedPrompt });
        }

        var aiRequest = new AiRequest
        {
            Model = providerConfig.SelectedModel,
            Messages = messages,
            SystemPrompt = systemPrompt,
            Stream = providerConfig.Streaming && progress != null,
            Tools = AiToolRegistry.GetToolDefinitions()
        };

        // 4. Stream or Complete Execution
        if (aiRequest.Stream && progress != null)
        {
            var fullResponse = new AiResponse();
            await foreach (var chunk in provider.StreamAsync(aiRequest, cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    fullResponse.Content += chunk.TextDelta;
                }
                progress.Report(chunk);
            }
            return fullResponse;
        }
        else
        {
            return await provider.CompleteAsync(aiRequest, cancellationToken);
        }
    }
}
