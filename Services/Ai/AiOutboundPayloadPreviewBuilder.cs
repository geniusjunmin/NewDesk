using System.Text;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class AiOutboundPayloadPreviewBuilder
{
    public static CloudSendPreview BuildPreview(
        string providerName,
        string model,
        string endpointHost,
        AiDataCategory categories,
        string sanitizedPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[模型] {model}");
        sb.AppendLine($"[数据类型] {categories}");
        sb.AppendLine("[提示词] " + (sanitizedPrompt.Length > 150 ? sanitizedPrompt[..150] + "..." : sanitizedPrompt));

        return new CloudSendPreview
        {
            ProviderName = providerName,
            Model = model,
            EndpointHost = endpointHost,
            DataSensitivity = DataSensitivity.Personal,
            IncludesReminderContext = categories.HasFlag(AiDataCategory.Reminder),
            IncludesWallpaperContext = categories.HasFlag(AiDataCategory.Wallpaper),
            IncludesDynamicDataContext = categories.HasFlag(AiDataCategory.DynamicData),
            IncludesClipboard = categories.HasFlag(AiDataCategory.Clipboard),
            SanitizedPreview = sb.ToString()
        };
    }
}
