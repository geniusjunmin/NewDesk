using System;
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
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public string? DynamicType { get; set; } // "GregorianDate", "LunarDate", "DayOfWeek", "Api", or null/Custom
    public double X { get; set; }
    public double Y { get; set; }
    public double FontSize { get; set; } = 36;
    public string FontFamily { get; set; } = "Microsoft YaHei";
    public string Color { get; set; } = "#FFFFFF";

    public bool Bold { get; set; } = false;
    public bool Italic { get; set; } = false;
    public bool Underline { get; set; } = false;
    public string Alignment { get; set; } = "Left"; // "Left", "Center", "Right"
    public int ZIndex { get; set; } = 0;

    // Advanced Visual Effects
    public bool ShadowEnabled { get; set; } = false;
    public string ShadowColor { get; set; } = "#000000";
    public double ShadowOpacity { get; set; } = 0.5;
    public double ShadowBlur { get; set; } = 5;
    public double ShadowOffsetX { get; set; } = 2;
    public double ShadowOffsetY { get; set; } = 2;

    public bool StrokeEnabled { get; set; } = false;
    public string StrokeColor { get; set; } = "#000000";
    public double StrokeThickness { get; set; } = 2;

    public bool BackgroundEnabled { get; set; } = false;
    public string BackgroundColor { get; set; } = "#000000";
    public double BackgroundOpacity { get; set; } = 0.5;
    public double BackgroundPadding { get; set; } = 8;
    public double BackgroundCornerRadius { get; set; } = 4;

    // Properties for API-driven text
    public string? ApiUrl { get; set; }
    public string? ApiRegex { get; set; }
    public string? ApiFormatting { get; set; }
    public string? ApiPrefix { get; set; }
    public string? ApiSuffix { get; set; }
    public string? DateFormat { get; set; }

    public TextElementState Clone()
    {
        return new TextElementState
        {
            Id = Guid.NewGuid(),
            Text = this.Text,
            DynamicType = this.DynamicType,
            X = this.X + 20,
            Y = this.Y + 20,
            FontSize = this.FontSize,
            FontFamily = this.FontFamily,
            Color = this.Color,
            Bold = this.Bold,
            Italic = this.Italic,
            Underline = this.Underline,
            Alignment = this.Alignment,
            ZIndex = this.ZIndex + 1,
            ShadowEnabled = this.ShadowEnabled,
            ShadowColor = this.ShadowColor,
            ShadowOpacity = this.ShadowOpacity,
            ShadowBlur = this.ShadowBlur,
            ShadowOffsetX = this.ShadowOffsetX,
            ShadowOffsetY = this.ShadowOffsetY,
            StrokeEnabled = this.StrokeEnabled,
            StrokeColor = this.StrokeColor,
            StrokeThickness = this.StrokeThickness,
            BackgroundEnabled = this.BackgroundEnabled,
            BackgroundColor = this.BackgroundColor,
            BackgroundOpacity = this.BackgroundOpacity,
            BackgroundPadding = this.BackgroundPadding,
            BackgroundCornerRadius = this.BackgroundCornerRadius,
            ApiUrl = this.ApiUrl,
            ApiRegex = this.ApiRegex,
            ApiFormatting = this.ApiFormatting,
            ApiPrefix = this.ApiPrefix,
            ApiSuffix = this.ApiSuffix,
            DateFormat = this.DateFormat
        };
    }
}
