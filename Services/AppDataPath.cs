using System;
using System.IO;

namespace NewDesk.Services;

public static class AppDataPath
{
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NewDesk"
    );

    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NewDesk",
        "Logs"
    );

    public static string SettingsFile => Path.Combine(DataFolder, "app_settings.json");
    public static string PasswordsFile => Path.Combine(DataFolder, "passwords.json");
    public static string RemindersFile => Path.Combine(DataFolder, "reminders.json");
    public static string WallpapersFile => Path.Combine(DataFolder, "wallpapers.json");
    public static string LogFile => Path.Combine(LogFolder, "newdesk.log");

    public static void Initialize()
    {
        try
        {
            if (!Directory.Exists(DataFolder))
            {
                Directory.CreateDirectory(DataFolder);
            }
            if (!Directory.Exists(LogFolder))
            {
                Directory.CreateDirectory(LogFolder);
            }

            // Automatic Migration from AppContext.BaseDirectory if needed
            MigrateFileIfNeeded("app_settings.json", SettingsFile);
            MigrateFileIfNeeded("passwords.json", PasswordsFile);
            MigrateFileIfNeeded("reminders.json", RemindersFile);
            MigrateFileIfNeeded("wallpapers.json", WallpapersFile);
        }
        catch (Exception ex)
        {
            LogError("AppDataPath.Initialize", ex);
        }
    }

    private static void MigrateFileIfNeeded(string fileName, string targetPath)
    {
        try
        {
            string oldPath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(oldPath) && !File.Exists(targetPath))
            {
                File.Copy(oldPath, targetPath, overwrite: false);
                LogInfo($"Migrated legacy data file {fileName} to {targetPath}");
            }
        }
        catch (Exception ex)
        {
            LogError($"MigrateFileIfNeeded ({fileName})", ex);
        }
    }

    public static void LogInfo(string message)
    {
        WriteLog("INFO", message);
    }

    public static void LogError(string context, Exception ex)
    {
        WriteLog("ERROR", $"[{context}] {ex.Message}\n{ex.StackTrace}");
    }

    private static void WriteLog(string level, string message)
    {
        try
        {
            if (!Directory.Exists(LogFolder))
            {
                Directory.CreateDirectory(LogFolder);
            }
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(LogFile, entry);
        }
        catch
        {
            // Silent fallback
        }
    }
}
