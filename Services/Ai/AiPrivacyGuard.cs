using System;
using NewDesk.Models;
using NewDesk.Models.Ai;
using NewDesk.Services.Security;

namespace NewDesk.Services.Ai;

public enum DataSensitivity
{
    Public,
    Personal,
    Sensitive,
    Secret
}

public static class AiPrivacyGuard
{
    public static void ValidateRequest(AiProviderConfig provider, DataSensitivity sensitivity, string rawPrompt)
    {
        var settings = SettingsService.LoadSettings();

        // 1. Secret Protection Rule (Absolute Prohibition)
        if (sensitivity == DataSensitivity.Secret || rawPrompt.Contains("MasterPassword") || rawPrompt.Contains("allpaw"))
        {
            throw new InvalidOperationException("【隐私防护动效】安全决策阻断：包含绝密凭据/主密码，禁止发送至任何 AI！");
        }

        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(provider.BaseUrl);

        // 2. Local-Only Mode Enforcement
        if (settings.AiNetworkMode == AiNetworkMode.LocalOnly && !isLocal)
        {
            throw new InvalidOperationException("【隐私防护动效】当前已设定为【仅限本地模型模式】，禁止向云端 AI 发送网络请求。");
        }

        // 3. Sensitive Data Classification Enforcement
        if (sensitivity == DataSensitivity.Sensitive && !isLocal && !settings.AllowAiPasswordMetadata)
        {
            throw new InvalidOperationException("【隐私防护动效】当前设置未授权向云端发送敏感元数据。");
        }
    }
}
