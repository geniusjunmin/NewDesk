using System.Windows.Input;

namespace NewDesk.Models;

public enum ReminderFrequency
{
    OnceADay,
    Hourly,
    HalfHourly,
    Minutely
}

public enum WallpaperRotationMode
{
    Sequential,
    Random
}

public enum StartupBehavior
{
    Normal,
    Tray
}

public enum CloseBehavior
{
    Tray,
    Exit,
    Ask
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum AiNetworkMode
{
    LocalOnly,
    AskBeforeCloud,
    AllowCloud
}

public class AppSettings
{
    // Legacy / Existing fields
    public uint HotkeyModifiers { get; set; }
    public Key Hotkey { get; set; }
    public ReminderFrequency ReminderFrequency { get; set; }
    public string? CurrentWallpaperName { get; set; }
    public bool IsWallpaperRotationEnabled { get; set; }
    public int WallpaperRotationIntervalMinutes { get; set; }
    public WallpaperRotationMode WallpaperRotationMode { get; set; }

    // Configurable Hotkeys
    public HotkeyBinding MainWindowHotkey { get; set; } = new((uint)(Services.HotkeyModifiers.Ctrl | Services.HotkeyModifiers.Alt), Key.D);
    public HotkeyBinding AiQuickHotkey { get; set; } = new((uint)(Services.HotkeyModifiers.Ctrl | Services.HotkeyModifiers.Shift), Key.Space);
    public HotkeyBinding CommandPaletteHotkey { get; set; } = new((uint)Services.HotkeyModifiers.Ctrl, Key.K);
    public HotkeyBinding ClipboardAiHotkey { get; set; } = new((uint)(Services.HotkeyModifiers.Ctrl | Services.HotkeyModifiers.Shift), Key.A);

    // New configuration fields with backward compatible defaults
    public bool HasCompletedSetupWizard { get; set; } = false;
    public StartupBehavior StartupBehavior { get; set; } = StartupBehavior.Tray;
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Tray;
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public int AutoLockMinutes { get; set; } = 15;
    public bool LockOnWindowsLock { get; set; } = true;

    public bool EnablePasswords { get; set; } = true;
    public bool EnableReminders { get; set; } = true;
    public bool EnableWallpaper { get; set; } = true;
    public bool EnableDynamicInfo { get; set; } = false;
    public bool EnableNotifications { get; set; } = true;

    public bool AutoClearClipboard { get; set; } = true;
    public int AutoClearClipboardSeconds { get; set; } = 30;

    // AI Privacy Defaults
    public bool EnableAiAssistant { get; set; } = true;
    public AiNetworkMode AiNetworkMode { get; set; } = AiNetworkMode.AskBeforeCloud;
    public bool AllowAiReminderContext { get; set; } = false;
    public bool AllowAiWallpaperContext { get; set; } = false;
    public bool AllowAiDynamicDataContext { get; set; } = false;
    public bool AllowAiPasswordMetadata { get; set; } = false;
    public bool AllowAiLogAnalysis { get; set; } = false;
    public bool AllowAiClipboard { get; set; } = false;
    public bool AllowCloudFallback { get; set; } = false;
    public bool AllowBackgroundCloudRequests { get; set; } = false;

    public string LastRunVersion { get; set; } = "2.2.1";

    public AppSettings()
    {
        HotkeyModifiers = (uint)(Services.HotkeyModifiers.Ctrl | Services.HotkeyModifiers.Alt);
        Hotkey = Key.D;
        ReminderFrequency = ReminderFrequency.OnceADay;
        WallpaperRotationIntervalMinutes = 30;
        WallpaperRotationMode = WallpaperRotationMode.Sequential;
    }
}
