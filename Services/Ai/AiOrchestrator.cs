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

public enum CloudRequestMode
{
    Interactive,
    PreAuthorizedBackground,
    LocalOnly
}

public class AiTurnRequest
{
    public AiTaskProfile TaskProfile { get; set; } = AiTaskProfile.GeneralChat;
    public string UserPrompt { get; set; } = string.Empty;
    public List<AiMessage> ConversationHistory { get; set; } = new();
    public AiProviderConfig? PreferredProvider { get; set; }
    public string? ModelOverride { get; set; }
    public DataSensitivity DataSensitivity { get; set; } = DataSensitivity.Personal;
    public AiDataCategory DataCategories { get; set; } = AiDataCategory.UserPrompt;
    public AiDataCategory RequestedContextCategories { get; set; } = AiDataCategory.None;
    public CloudRequestMode CloudRequestMode { get; set; } = CloudRequestMode.Interactive;
    public Func<PendingToolAction, Task<bool>>? ConfirmationCallback { get; set; }
    public Func<CloudSendPreview, Task<bool>>? CloudConsentCallback { get; set; }
    public IProgress<ToolExecutionDisplayInfo>? ToolExecutionProgress { get; set; }
    public bool EnableTools { get; set; } = false;
    public HashSet<string> AllowedToolNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

        // 2. Pre-Send Security Endpoint Validation
        string? secret = SecretStorageService.GetSecret(providerConfig.SecretId);
        bool transmitsSecrets = !string.IsNullOrEmpty(secret);
        AiHttpSecurityGuard.ValidateRequest(providerConfig.BaseUrl, transmitsSecrets);

        // 3. Build Outbound Messages Payload with Redaction
        var outboundMessages = AiOutboundContextBuilder.BuildMessages(turnRequest.ConversationHistory, turnRequest.UserPrompt);

        // 4. Build Context
        var contextResult = AiContextBroker.BuildContextPrompt(turnRequest.RequestedContextCategories);

        // 5. Calculate Actual Categories
        var actualCategories = turnRequest.DataCategories | contextResult.IncludedCategories;

        // 6. Privacy Guard Validation
        string sanitizedPrompt = SecretRedactor.Redact(turnRequest.UserPrompt);
        AiPrivacyGuard.ValidateRequest(providerConfig, turnRequest.DataSensitivity, actualCategories, sanitizedPrompt);

        // 7. System Prompt Construction
        string systemPrompt = string.IsNullOrEmpty(contextResult.Prompt)
            ? "你是一个专业、高效的 Windows 桌面 AI 助手。"
            : $"你是一个专业、高效的 Windows 桌面 AI 助手。\n\n{contextResult.Prompt}";

        // 8. Cloud Mode & Consent Handling
        var settings = SettingsService.LoadSettings();
        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(providerConfig.BaseUrl);

        if (turnRequest.CloudRequestMode == CloudRequestMode.LocalOnly && !isLocal)
        {
            throw new InvalidOperationException("当前请求配置为仅限本地模型模式，取消云端请求。");
        }

        if (!isLocal)
        {
            if (turnRequest.CloudRequestMode == CloudRequestMode.Interactive)
            {
                if (settings.AiNetworkMode == AiNetworkMode.AskBeforeCloud)
                {
                    if (turnRequest.CloudConsentCallback == null)
                    {
                        throw new InvalidOperationException("当前网络模式为【云端发送前询问】，但未提供云端发送确认接口。请求已取消。");
                    }

                    string host = "cloud";
                    try { host = new Uri(providerConfig.BaseUrl).Host; } catch { }

                    var preview = AiOutboundPayloadPreviewBuilder.BuildPreview(
                        providerConfig.Name,
                        turnRequest.ModelOverride ?? providerConfig.SelectedModel,
                        host,
                        turnRequest.DataSensitivity,
                        actualCategories,
                        systemPrompt,
                        outboundMessages,
                        sanitizedPrompt);

                    bool consentGranted = await turnRequest.CloudConsentCallback(preview);
                    if (!consentGranted)
                    {
                        throw new InvalidOperationException("用户取消了发送数据至云端 AI 的操作。");
                    }
                }
            }
            else if (turnRequest.CloudRequestMode == CloudRequestMode.PreAuthorizedBackground)
            {
                if (!settings.AllowBackgroundCloudRequests)
                {
                    throw new InvalidOperationException("后台云端 AI 请求未获授权。请在设置中开启【允许后台 Cloud AI】。");
                }
            }
        }

        var provider = AiProviderFactory.CreateProvider(providerConfig);
        var toolDefs = GetAllowedToolDefinitions(turnRequest, provider.Capabilities.SupportsTools);

        int currentRound = 0;
        AiResponse finalResponse = new();

        while (currentRound < MaxToolRounds)
        {
            currentRound++;

            var aiRequest = new AiRequest
            {
                Model = turnRequest.ModelOverride ?? providerConfig.SelectedModel,
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
            finalResponse.ToolRounds = currentRound - 1;

            // Check if model requested tool execution
            if (roundResponse.ToolCalls.Count == 0)
            {
                return finalResponse;
            }

            outboundMessages.Add(new AiMessage
            {
                Role = "assistant",
                Content = roundResponse.Content,
                ToolCalls = roundResponse.ToolCalls
            });

            foreach (var toolCall in roundResponse.ToolCalls)
            {
                AiToolResult toolResult;
                if (!IsToolAllowed(turnRequest, toolCall.Name))
                {
                    toolResult = new AiToolResult
                    {
                        ToolCallId = toolCall.Id,
                        IsError = true,
                        OutputJson = JsonSerializer.Serialize(new { success = false, error = "tool_not_allowed" })
                    };
                }
                else
                {
                    var validation = AiToolArgumentValidator.ValidateArguments(toolCall.Name, toolCall.ArgumentsJson);
                    if (!validation.IsValid)
                    {
                        toolResult = new AiToolResult
                        {
                            ToolCallId = toolCall.Id,
                            IsError = true,
                            OutputJson = JsonSerializer.Serialize(new { success = false, error = $"工具参数校验失败: {validation.ErrorMessage}" })
                        };
                    }
                    else if (toolCall.Name.Equals("get_system_info", StringComparison.OrdinalIgnoreCase))
                    {
                        toolResult = await ExecuteSystemInfoToolAsync(
                            toolCall,
                            turnRequest,
                            providerConfig,
                            isLocal,
                            actualCategories,
                            systemPrompt,
                            outboundMessages,
                            sanitizedPrompt);
                    }
                    else
                    {
                        toolResult = await AiToolExecutionService.ExecuteToolWithPermissionAsync(toolCall, turnRequest.ConfirmationCallback);
                    }
                }

                // Report ToolExecutionDisplayInfo to UI progress
                var displayInfo = ToolExecutionDisplayInfo.Format(toolCall.Name, toolCall.ArgumentsJson, !toolResult.IsError);
                turnRequest.ToolExecutionProgress?.Report(displayInfo);

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
        finalResponse.ToolRounds = MaxToolRounds;
        return finalResponse;
    }

    internal static List<AiToolDefinition> GetAllowedToolDefinitions(AiTurnRequest request, bool providerSupportsTools)
    {
        return request.EnableTools && providerSupportsTools
            ? AiToolRegistry.GetToolDefinitions(request.AllowedToolNames)
            : [];
    }

    internal static bool IsToolAllowed(AiTurnRequest request, string toolName)
    {
        return request.EnableTools && request.AllowedToolNames.Contains(toolName);
    }

    internal static async Task<AiToolResult> ExecuteSystemInfoToolAsync(
        AiToolCall toolCall,
        AiTurnRequest turnRequest,
        AiProviderConfig providerConfig,
        bool isLocal,
        AiDataCategory actualCategories,
        string systemPrompt,
        List<AiMessage> outboundMessages,
        string sanitizedPrompt)
    {
        var settings = SettingsService.LoadSettings();
        if (!settings.AllowAiSystemInfo)
        {
            return ToolError(toolCall, "system_info_not_allowed");
        }

        if (!isLocal)
        {
            if (turnRequest.CloudConsentCallback == null)
            {
                return ToolError(toolCall, "system_info_cloud_consent_required");
            }

            string host = "cloud";
            try { host = new Uri(providerConfig.BaseUrl).Host; } catch { }
            var preview = AiOutboundPayloadPreviewBuilder.BuildPreview(
                providerConfig.Name,
                turnRequest.ModelOverride ?? providerConfig.SelectedModel,
                host,
                turnRequest.DataSensitivity,
                actualCategories | AiDataCategory.SystemInfo,
                systemPrompt,
                outboundMessages,
                "AI 请求读取并向云端发送系统设备信息：OS、MachineName、ProcessorCount、.NET Version。\n" + sanitizedPrompt);

            if (!await turnRequest.CloudConsentCallback(preview))
            {
                return ToolError(toolCall, "system_info_cloud_consent_denied");
            }
        }

        return await AiToolExecutionService.ExecuteToolWithPermissionAsync(toolCall, turnRequest.ConfirmationCallback);
    }

    private static AiToolResult ToolError(AiToolCall toolCall, string error)
    {
        return new AiToolResult
        {
            ToolCallId = toolCall.Id,
            IsError = true,
            OutputJson = JsonSerializer.Serialize(new { success = false, error })
        };
    }
}
