using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewDesk.Models;
using NewDesk.Models.Ai;
using NewDesk.Services.Ai.Tools;
using NewDesk.Services.Security;

namespace NewDesk.Services.Ai;

public class AiTurnRequest
{
    public AiTaskProfile TaskProfile { get; set; } = AiTaskProfile.GeneralChat;
    public string UserPrompt { get; set; } = string.Empty;
    public List<AiMessage> ConversationHistory { get; set; } = new();
    public AiProviderConfig? PreferredProvider { get; set; }
    public DataSensitivity DataSensitivity { get; set; } = DataSensitivity.Personal;
    public Func<PendingToolAction, Task<bool>>? ConfirmationCallback { get; set; }
    public Func<CloudSendPreview, Task<bool>>? CloudConsentCallback { get; set; }
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

        // 3. AskBeforeCloud Consent Check
        var settings = SettingsService.LoadSettings();
        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(providerConfig.BaseUrl);

        if (!isLocal && settings.AiNetworkMode == AiNetworkMode.AskBeforeCloud)
        {
            if (turnRequest.CloudConsentCallback == null)
            {
                throw new InvalidOperationException("当前网络模式为【云端发送前询问】，但未提供云端发送确认接口。请求已取消。");
            }

            string host = "cloud";
            try { host = new Uri(providerConfig.BaseUrl).Host; } catch { }

            var preview = new CloudSendPreview
            {
                ProviderName = providerConfig.Name,
                Model = providerConfig.SelectedModel,
                EndpointHost = host,
                DataSensitivity = turnRequest.DataSensitivity,
                IncludesReminderContext = settings.AllowAiReminderContext,
                IncludesWallpaperContext = settings.AllowAiWallpaperContext,
                IncludesDynamicDataContext = settings.AllowAiDynamicDataContext,
                IncludesClipboard = settings.AllowAiClipboard,
                SanitizedPreview = sanitizedPrompt.Length > 200 ? sanitizedPrompt[..200] + "..." : sanitizedPrompt
            };

            bool consentGranted = await turnRequest.CloudConsentCallback(preview);
            if (!consentGranted)
            {
                throw new InvalidOperationException("用户取消了云端 AI 发送授权。");
            }
        }

        // 4. Inject Context
        string contextPrompt = AiContextBroker.BuildContextPrompt();
        string systemPrompt = string.IsNullOrEmpty(contextPrompt)
            ? "你是一个专业、高效的 Windows 桌面 AI 助手。"
            : $"你是一个专业、高效的 Windows 桌面 AI 助手。\n\n{contextPrompt}";

        var provider = AiProviderFactory.CreateProvider(providerConfig);

        // 5. Build Outbound Messages Payload with Redaction
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
                Tools = toolDefs ?? new List<AiToolDefinition>()
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
                    if (chunk.ToolCalls.Count > 0)
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
            if (roundResponse.ToolCalls.Count == 0)
            {
                return finalResponse;
            }

            // Append assistant tool-call turn
            outboundMessages.Add(new AiMessage
            {
                Role = "assistant",
                Content = roundResponse.Content,
                ToolCalls = roundResponse.ToolCalls
            });

            foreach (var toolCall in roundResponse.ToolCalls)
            {
                // Validate Tool Arguments before execution
                var validation = AiToolArgumentValidator.ValidateArguments(toolCall.Name, toolCall.ArgumentsJson);
                AiToolResult toolResult;

                if (!validation.IsValid)
                {
                    toolResult = new AiToolResult
                    {
                        ToolCallId = toolCall.Id,
                        IsError = true,
                        OutputJson = JsonSerializer.Serialize(new { success = false, error = $"工具参数校验失败: {validation.ErrorMessage}" })
                    };
                }
                else
                {
                    toolResult = await AiToolExecutionService.ExecuteToolWithPermissionAsync(toolCall, turnRequest.ConfirmationCallback);
                }

                // Redact secrets from Tool Output before submitting back to model
                string sanitizedOutput = SecretRedactor.Redact(toolResult.OutputJson);

                outboundMessages.Add(new AiMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = sanitizedOutput
                });
            }
        }

        AppDataPath.LogInfo($"AiOrchestrator Tool Loop reached maximum safe rounds limit ({MaxToolRounds}).");
        finalResponse.Content += "\n[系统提示: AI 工具调用轮次已达到安全限制]";
        return finalResponse;
    }
}
