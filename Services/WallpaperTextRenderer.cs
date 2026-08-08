using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NewDesk.Models;

namespace NewDesk.Services;

public static class WallpaperTextRenderer
{
    public static void DrawElement(
        DrawingContext dc,
        TextElementState element,
        string apiResultText,
        double scaleX,
        double scaleY,
        double offsetX = 0,
        double offsetY = 0)
    {
        if (!element.IsVisible) return;

        string textToRender = element.Text;
        if (element.DynamicType == "GregorianDate") textToRender = GetGregorianDateString(element.DateFormat);
        else if (element.DynamicType == "LunarDate") textToRender = GetLunarDateString();
        else if (element.DynamicType == "DayOfWeek") textToRender = DateTime.Now.ToString("dddd", new CultureInfo("zh-CN"));
        else if ((element.DynamicType == "Api" || element.DynamicType == "DataSource" || !string.IsNullOrEmpty(element.DataSourceId)) && !string.IsNullOrEmpty(apiResultText))
        {
            textToRender = apiResultText;
        }

        if (string.IsNullOrEmpty(textToRender)) return;

        double fontScale = Math.Min(scaleX, scaleY);
        double fontSize = Math.Max(1.0, element.FontSize * fontScale);

        var style = element.Italic ? FontStyles.Italic : FontStyles.Normal;
        var weight = element.Bold ? FontWeights.Bold : FontWeights.Normal;
        var typeface = new Typeface(new FontFamily(element.FontFamily), style, weight, FontStretches.Normal);

        Color textColor;
        try { textColor = (Color)ColorConverter.ConvertFromString(element.Color); }
        catch { textColor = Colors.White; }
        var mainBrush = new SolidColorBrush(textColor);

        var formattedText = new FormattedText(
            textToRender,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            mainBrush,
            96.0);

        if (element.Underline)
        {
            formattedText.SetTextDecorations(TextDecorations.Underline);
        }

        formattedText.TextAlignment = element.Alignment switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        double x = offsetX + (element.X * scaleX);
        double y = offsetY + (element.Y * scaleY);
        Point location = new Point(x, y);

        // 1. Draw Background Box if enabled
        if (element.BackgroundEnabled)
        {
            Color bgColor;
            try { bgColor = (Color)ColorConverter.ConvertFromString(element.BackgroundColor); }
            catch { bgColor = Colors.Black; }
            bgColor.A = (byte)(Math.Clamp(element.BackgroundOpacity, 0, 1) * 255);
            var bgBrush = new SolidColorBrush(bgColor);

            double pad = element.BackgroundPadding * fontScale;
            Rect bgRect = new Rect(
                x - pad,
                y - pad,
                formattedText.Width + (pad * 2),
                formattedText.Height + (pad * 2));

            double rx = element.BackgroundCornerRadius * fontScale;
            dc.DrawRoundedRectangle(bgBrush, null, bgRect, rx, rx);
        }

        // 2. Draw Shadow if enabled
        if (element.ShadowEnabled)
        {
            Color shadowColor;
            try { shadowColor = (Color)ColorConverter.ConvertFromString(element.ShadowColor); }
            catch { shadowColor = Colors.Black; }
            shadowColor.A = (byte)(Math.Clamp(element.ShadowOpacity, 0, 1) * 255);
            var shadowBrush = new SolidColorBrush(shadowColor);

            var shadowText = new FormattedText(
                textToRender,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                shadowBrush,
                96.0)
            {
                TextAlignment = formattedText.TextAlignment
            };
            if (element.Underline) shadowText.SetTextDecorations(TextDecorations.Underline);

            Point shadowLoc = new Point(
                x + (element.ShadowOffsetX * fontScale),
                y + (element.ShadowOffsetY * fontScale));

            dc.DrawText(shadowText, shadowLoc);
        }

        // 3. Draw Stroke if enabled
        if (element.StrokeEnabled && element.StrokeThickness > 0)
        {
            Color strokeColor;
            try { strokeColor = (Color)ColorConverter.ConvertFromString(element.StrokeColor); }
            catch { strokeColor = Colors.Black; }
            var strokePen = new Pen(new SolidColorBrush(strokeColor), element.StrokeThickness * fontScale);

            Geometry textGeom = formattedText.BuildGeometry(location);
            dc.DrawGeometry(mainBrush, strokePen, textGeom);
        }
        else
        {
            dc.DrawText(formattedText, location);
        }
    }

    private static string GetGregorianDateString(string? format = null)
    {
        if (string.IsNullOrEmpty(format)) format = "yyyy-MM-dd";
        try { return DateTime.Now.ToString(format, CultureInfo.CurrentCulture); }
        catch { return DateTime.Now.ToString("yyyy-MM-dd"); }
    }

    private static string GetLunarDateString()
    {
        try
        {
            var calendar = new ChineseLunisolarCalendar();
            var date = DateTime.Now;
            var year = calendar.GetYear(date);
            var month = calendar.GetMonth(date);
            var day = calendar.GetDayOfMonth(date);
            var leapMonth = calendar.GetLeapMonth(year);

            string[] lunarMonthNames = { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
            string[] lunarDayNames = { "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十", "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十", "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十" };

            string monthString;
            if (leapMonth > 0 && month >= leapMonth)
            {
                if (month == leapMonth)
                {
                    int prevMonthIndex = month - 2;
                    monthString = (prevMonthIndex >= 0 && prevMonthIndex < lunarMonthNames.Length) ? "闰" + lunarMonthNames[prevMonthIndex] : "闰月";
                }
                else
                {
                    int realMonthIndex = month - 2;
                    monthString = (realMonthIndex >= 0 && realMonthIndex < lunarMonthNames.Length) ? lunarMonthNames[realMonthIndex] : "未知月";
                }
            }
            else
            {
                monthString = (month - 1 >= 0 && month - 1 < lunarMonthNames.Length) ? lunarMonthNames[month - 1] : "未知月";
            }

            string dayString = (day - 1 >= 0 && day - 1 < lunarDayNames.Length) ? lunarDayNames[day - 1] : "未知日";
            return $"农历 {monthString}{dayString}";
        }
        catch { return "农历 八月十五"; }
    }
}
