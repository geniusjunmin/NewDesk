using System.Windows.Input;

namespace NewDesk.Models;

public enum ReminderFrequency
{
    OnceADay,
    Hourly,
    HalfHourly,
    Minutely
}

public class AppSettings
{
    public uint HotkeyModifiers { get; set; }
    public Key Hotkey { get; set; }
    public ReminderFrequency ReminderFrequency { get; set; }
    public string? CurrentWallpaperName { get; set; }

    public bool IsWallpaperRotationEnabled { get; set; }
    public int WallpaperRotationIntervalMinutes { get; set; }
    public WallpaperRotationMode WallpaperRotationMode { get; set; }

    public AppSettings()
    {
        HotkeyModifiers = (uint)(Services.HotkeyModifiers.Ctrl | Services.HotkeyModifiers.Alt);
        Hotkey = Key.D;
        ReminderFrequency = ReminderFrequency.OnceADay;
        WallpaperRotationIntervalMinutes = 30; // Default 30 mins
        WallpaperRotationMode = WallpaperRotationMode.Sequential;
    }
}

public enum WallpaperRotationMode
{
    Sequential,
    Random
}
