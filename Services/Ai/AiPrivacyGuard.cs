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

[Flags]
public enum AiDataCategory
{
    None = 0,
    UserPrompt = 1,
    Reminder = 2,
    Wallpaper = 4,
    DynamicData = 8,
    PasswordMetadata = 16,
    Logs = 32,
    Clipboard = 64
}

public static class AiPrivacyGuard
{
    public static void ValidateRequest(
        AiProviderConfig provider,
        DataSensitivity sensitivity,
        AiDataCategory categories,
        string rawPrompt)
    {
        var settings = SettingsService.LoadSettings();

        // 1. Secret Protection Rule (Absolute Prohibition)
        if (sensitivity == DataSensitivity.Secret || rawPrompt.Contains("MasterPassword") || rawPrompt.Contains("allpaw"))
        {
            throw new InvalidOperationException("【隐私防护动效】安全决策阻断：包含绝密凭据/主密码，禁止发送至任何 AI！");
        }

        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(provider.BaseUrl);

        // 2. Network Mode Enforcement (LocalOnly)
        if (settings.AiNetworkMode == AiNetworkMode.LocalOnly && !isLocal)
        {
            throw new InvalidOperationException("【隐私防护动效】当前已设定为【仅限本地模型模式】，禁止向云端 AI 发送网络请求。");
        }

        // 3. Category Permission Validation (Checked for both Local and Cloud endpoints)
        if (categories.HasFlag(AiDataCategory.Clipboard) && !settings.AllowAiClipboard)
        {
            throw new InvalidOperationException("【隐私防护动效】当前未授权 AI 访问剪贴板内容。");
        }
        if (categories.HasFlag(AiDataCategory.Reminder) && !settings.AllowAiReminderContext)
        {
            throw new InvalidOperationException("【隐私防护动效】当前未授权 AI 访问提醒事项上下文。");
        }
        if (categories.HasFlag(AiDataCategory.Wallpaper) && !settings.AllowAiWallpaperContext)
        {
            throw new InvalidOperationException("【隐私防护动效】当前未授权 AI 访问壁纸上下文。");
        }
        if (categories.HasFlag(AiDataCategory.DynamicData) && !settings.AllowAiDynamicDataContext)
        {
            throw new InvalidOperationException("【隐私防护动效】当前未授权 AI 访问动态数据上下文。");
        }
        if (categories.HasFlag(AiDataCategory.PasswordMetadata) && !settings.AllowAiPasswordMetadata)
        {
            throw new InvalidOperationException("【隐私防护动效】当前未授权 AI 访问密码元数据。");
        }
        if (categories.HasFlag(AiDataCategory.Logs) && !settings.AllowAiLogAnalysis)
        {
            throw new InvalidOperationException("【隐私防护动效】当前未授权 AI 访问日志内容。");
        }
    }
}
