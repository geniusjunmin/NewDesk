using NewDesk.Services.Ai;

namespace NewDesk.Models.Ai;

public class CloudSendPreview
{
    public string ProviderName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string EndpointHost { get; set; } = string.Empty;
    public DataSensitivity DataSensitivity { get; set; } = DataSensitivity.Personal;
    public bool IncludesReminderContext { get; set; }
    public bool IncludesWallpaperContext { get; set; }
    public bool IncludesDynamicDataContext { get; set; }
    public bool IncludesClipboard { get; set; }
    public string SanitizedPreview { get; set; } = string.Empty;
}
