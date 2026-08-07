using System;
using System.Text.RegularExpressions;
using NewDesk.Models;

namespace NewDesk.Services.Ai;

public static class AiPrivacyService
{
    public static bool AllowAiAccessToReminders { get; set; } = false;
    public static bool AllowAiAccessToWallpaperMetadata { get; set; } = false;
    public static bool AllowAiAccessToPasswordMetadata { get; set; } = false;
    public static bool AllowAiAccessToLogs { get; set; } = false;
    public static bool AllowCloudProviderFallback { get; set; } = false;
    public static bool OfflineOnlyMode { get; set; } = false;

    public static string SanitizeTextForAi(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return SecretRedactor.Redact(input);
    }

    public static void ValidatePrivacyBeforeRequest(string targetUrl, string? promptContent)
    {
        if (OfflineOnlyMode && (targetUrl.StartsWith("http://") || targetUrl.StartsWith("https://")) && !targetUrl.Contains("localhost") && !targetUrl.Contains("127.0.0.1"))
        {
            throw new InvalidOperationException("当前处于【仅本地 AI 模式】，已禁止向云端 AI 发送网络请求。");
        }

        if (!string.IsNullOrEmpty(promptContent))
        {
            if (promptContent.Contains("MasterPassword") || promptContent.Contains("allpaw") || promptContent.Contains("passwords.json"))
            {
                throw new InvalidOperationException("隐私机制阻止：禁止向 AI 发送主密码或密码库敏感凭据！");
            }
        }
    }
}
