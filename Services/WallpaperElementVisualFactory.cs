using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NewDesk.Models;

namespace NewDesk.Services;

public static class WallpaperElementVisualFactory
{
    public static TextBlock CreateTextBlock(TextElementState element, string text)
    {
        Color foreground;
        try { foreground = (Color)ColorConverter.ConvertFromString(element.Color); }
        catch { foreground = Colors.White; }

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = element.FontSize,
            FontFamily = new FontFamily(element.FontFamily),
            Foreground = new SolidColorBrush(foreground),
            FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = element.Italic ? FontStyles.Italic : FontStyles.Normal,
            Cursor = element.IsLocked ? Cursors.Arrow : Cursors.SizeAll,
            Tag = element,
            Padding = element.BackgroundEnabled ? new Thickness(Math.Max(0, element.BackgroundPadding)) : default
        };

        if (element.Underline) textBlock.TextDecorations = TextDecorations.Underline;
        textBlock.TextAlignment = element.Alignment switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        if (element.BackgroundEnabled)
        {
            Color background;
            try { background = (Color)ColorConverter.ConvertFromString(element.BackgroundColor); }
            catch { background = Colors.Black; }
            background.A = (byte)(Math.Clamp(element.BackgroundOpacity, 0, 1) * 255);
            textBlock.Background = new SolidColorBrush(background);
        }

        if (element.ShadowEnabled)
        {
            Color shadow;
            try { shadow = (Color)ColorConverter.ConvertFromString(element.ShadowColor); }
            catch { shadow = Colors.Black; }
            textBlock.Effect = new DropShadowEffect
            {
                Color = shadow,
                Opacity = Math.Clamp(element.ShadowOpacity, 0, 1),
                BlurRadius = Math.Max(0, element.ShadowBlur),
                Direction = 315,
                ShadowDepth = Math.Sqrt((element.ShadowOffsetX * element.ShadowOffsetX) + (element.ShadowOffsetY * element.ShadowOffsetY))
            };
        }

        return textBlock;
    }
}
