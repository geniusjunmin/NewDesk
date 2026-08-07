using System;
using System.IO;

namespace NewDesk.Services;

public static class AppEnvironment
{
    private static string? _customDataFolder;
    private static string? _customLogFolder;

    public static bool IsTestMode { get; private set; }

    public static string DataFolder
    {
        get
        {
            if (!string.IsNullOrEmpty(_customDataFolder)) return _customDataFolder;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NewDesk"
            );
        }
    }

    public static string LogFolder
    {
        get
        {
            if (!string.IsNullOrEmpty(_customLogFolder)) return _customLogFolder;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NewDesk",
                "Logs"
            );
        }
    }

    public static void SetTestEnvironment(string testRoot)
    {
        IsTestMode = true;
        _customDataFolder = Path.Combine(testRoot, "Data");
        _customLogFolder = Path.Combine(testRoot, "Logs");

        Directory.CreateDirectory(_customDataFolder);
        Directory.CreateDirectory(_customLogFolder);
    }

    public static void ResetToNormalEnvironment()
    {
        IsTestMode = false;
        _customDataFolder = null;
        _customLogFolder = null;
    }
}
