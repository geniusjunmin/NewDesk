using System.Windows;
using System.Windows.Media;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Controls;

public class WallpaperTextElementControl : FrameworkElement
{
    public static readonly DependencyProperty ElementStateProperty =
        DependencyProperty.Register(nameof(ElementState), typeof(TextElementState), typeof(WallpaperTextElementControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ApiResultTextProperty =
        DependencyProperty.Register(nameof(ApiResultText), typeof(string), typeof(WallpaperTextElementControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public TextElementState? ElementState
    {
        get => (TextElementState?)GetValue(ElementStateProperty);
        set => SetValue(ElementStateProperty, value);
    }

    public string ApiResultText
    {
        get => (string)GetValue(ApiResultTextProperty);
        set => SetValue(ApiResultTextProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ElementState == null) return;

        WallpaperTextRenderer.DrawElement(
            dc,
            ElementState,
            ApiResultText,
            scaleX: 1.0,
            scaleY: 1.0,
            offsetX: 0,
            offsetY: 0
        );
    }
}
