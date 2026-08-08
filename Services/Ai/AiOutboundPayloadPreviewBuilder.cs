using System;
using System.Collections.Generic;
using System.Text;
using NewDesk.Models.Ai;
using NewDesk.Services.Security;

namespace NewDesk.Services.Ai;

public static class AiOutboundPayloadPreviewBuilder
{
    public static CloudSendPreview BuildPreview(
        string providerName,
        string model,
        string endpointHost,
        DataSensitivity dataSensitivity,
        AiDataCategory actualCategories,
        string? systemPrompt,
        List<AiMessage>? outboundMessages,
        string currentPrompt)
    {
        string redactedCurrentPrompt = SecretRedactor.Redact(currentPrompt);
        string redactedSystemPrompt = !string.IsNullOrEmpty(systemPrompt) ? SecretRedactor.Redact(systemPrompt) : "";

        var sb = new StringBuilder();
        sb.AppendLine($"[服务提供商] {providerName}");
        sb.AppendLine($"[模型] {model}");
        sb.AppendLine($"[目标主机] {endpointHost}");
        sb.AppendLine($"[敏感等级] {dataSensitivity}");
        sb.AppendLine($"[实际数据类别] {actualCategories}");

        if (!string.IsNullOrEmpty(redactedSystemPrompt))
        {
            string sysTruncated = redactedSystemPrompt.Length > 300 ? redactedSystemPrompt[..300] + "..." : redactedSystemPrompt;
            sb.AppendLine($"\n--- 系统提示词/上下文 ---\n{sysTruncated}");
        }

        if (outboundMessages != null && outboundMessages.Count > 0)
        {
            sb.AppendLine($"\n--- 对话历史 ({outboundMessages.Count} 条) ---");
            foreach (var msg in outboundMessages)
            {
                string redactedMsg = SecretRedactor.Redact(msg.Content);
                string msgTruncated = redactedMsg.Length > 150 ? redactedMsg[..150] + "..." : redactedMsg;
                sb.AppendLine($"[{msg.Role}]: {msgTruncated}");
            }
        }

        string promptTruncated = redactedCurrentPrompt.Length > 500 ? redactedCurrentPrompt[..500] + "..." : redactedCurrentPrompt;
        sb.AppendLine($"\n--- 当前提示词 ---\n{promptTruncated}");

        return new CloudSendPreview
        {
            ProviderName = providerName,
            Model = model,
            EndpointHost = endpointHost,
            DataSensitivity = dataSensitivity,
            IncludesReminderContext = actualCategories.HasFlag(AiDataCategory.Reminder),
            IncludesWallpaperContext = actualCategories.HasFlag(AiDataCategory.Wallpaper),
            IncludesDynamicDataContext = actualCategories.HasFlag(AiDataCategory.DynamicData),
            IncludesClipboard = actualCategories.HasFlag(AiDataCategory.Clipboard),
            IncludesSystemInfo = actualCategories.HasFlag(AiDataCategory.SystemInfo),
            SanitizedPreview = sb.ToString()
        };
    }
}
