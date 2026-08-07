using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ThemeMode = NewDesk.Models.ThemeMode;

namespace NewDesk.Services;

public static class ThemeManager
{
    private static bool _isHookedSystemTheme = false;
    private static ThemeMode _currentMode = ThemeMode.System;

    public static void ApplyTheme(ThemeMode mode)
    {
        _currentMode = mode;

        if (!_isHookedSystemTheme)
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (_currentMode == ThemeMode.System)
                {
                    Application.Current.Dispatcher.Invoke(() => SetTheme(IsSystemDarkTheme()));
                }
            };
            _isHookedSystemTheme = true;
        }

        if (mode == ThemeMode.System)
        {
            SetTheme(IsSystemDarkTheme());
        }
        else
        {
            SetTheme(mode == ThemeMode.Dark);
        }
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int useLightTheme)
            {
                return useLightTheme == 0;
            }
        }
        catch
        {
            // Default fallback
        }
        return true;
    }

    private static void SetTheme(bool isDark)
    {
        if (isDark)
        {
            SetColor("WindowBackground", "#111827");
            SetColor("SurfaceBackground", "#1F2937");
            SetColor("SurfaceSecondaryBackground", "#374151");
            SetColor("SurfaceHoverBackground", "#4B5563");
            SetColor("TextPrimary", "#F9FAFB");
            SetColor("TextSecondary", "#9CA3AF");
            SetColor("TextMuted", "#6B7280");
            SetColor("BorderBrushColor", "#374151");
            SetColor("InputBackground", "#111827");
            SetColor("SidebarBackground", "#111827");
            SetColor("SidebarItemHover", "#1F2937");
            SetColor("SidebarItemSelected", "#2563EB");
            SetColor("SidebarTextSelected", "#FFFFFF");
        }
        else
        {
            SetColor("WindowBackground", "#F3F4F6");
            SetColor("SurfaceBackground", "#FFFFFF");
            SetColor("SurfaceSecondaryBackground", "#E5E7EB");
            SetColor("SurfaceHoverBackground", "#D1D5DB");
            SetColor("TextPrimary", "#111827");
            SetColor("TextSecondary", "#4B5563");
            SetColor("TextMuted", "#9CA3AF");
            SetColor("BorderBrushColor", "#E5E7EB");
            SetColor("InputBackground", "#FFFFFF");
            SetColor("SidebarBackground", "#FFFFFF");
            SetColor("SidebarItemHover", "#F3F4F6");
            SetColor("SidebarItemSelected", "#2563EB");
            SetColor("SidebarTextSelected", "#FFFFFF");
        }
    }

    private static void SetColor(string key, string hexColor)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
        catch
        {
            // Fallback
        }
    }
}
