using System.Collections.Generic;

namespace NewDesk.Models;

public class WallpaperState
{
    public string Name { get; set; } = string.Empty;
    public string? BackgroundImagePath { get; set; }
    public List<TextElementState> TextElements { get; set; } = new();
    public int RefreshIntervalMinutes { get; set; } = 0; // 0 means disabled
    public double DesignWidth { get; set; } = 1920; // Default fallback
    public double DesignHeight { get; set; } = 1080; // Default fallback
}

public class TextElementState
{
    public string Text { get; set; } = string.Empty;
    public string? DynamicType { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double FontSize { get; set; }
    public string FontFamily { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    
    // Properties for API-driven text
    public string? ApiUrl { get; set; }
    public string? ApiRegex { get; set; }
    public string? ApiFormatting { get; set; }
    public string? ApiPrefix { get; set; }
    public string? ApiSuffix { get; set; }
}
