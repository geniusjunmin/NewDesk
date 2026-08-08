using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using NewDesk.Models;

namespace NewDesk.Services;

public static class WallpaperPreviewRenderer
{
    public static void DrawWallpaperPreview(
        DrawingContext dc,
        List<TextElementState> layers,
        Size renderSize,
        double designWidth = 1920,
        double designHeight = 1080,
        ImageSource? bgImage = null,
        WallpaperRenderContext? renderContext = null)
    {
        if (renderSize.Width <= 0 || renderSize.Height <= 0) return;

        // 1. Draw Background Image
        if (bgImage != null)
        {
            dc.DrawImage(bgImage, new Rect(0, 0, renderSize.Width, renderSize.Height));
        }

        // 2. Compute Scale Ratios
        double scaleX = renderSize.Width / Math.Max(1.0, designWidth);
        double scaleY = renderSize.Height / Math.Max(1.0, designHeight);

        // 3. Render Layers in ZIndex Order
        var sortedLayers = layers.OrderBy(l => l.ZIndex).ToList();
        foreach (var layer in sortedLayers)
        {
            string renderText = WallpaperTextRenderer.ResolveElementText(layer, renderContext);
            WallpaperTextRenderer.DrawElement(dc, layer, renderText, scaleX, scaleY);
        }
    }
}
