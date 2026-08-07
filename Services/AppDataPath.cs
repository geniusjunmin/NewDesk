using System;
using System.IO;

namespace NewDesk.Services;

public static class AppDataPath
{
    public static string DataFolder => AppEnvironment.DataFolder;
    public static string LogFolder => AppEnvironment.LogFolder;

    public static string SettingsFile => Path.Combine(DataFolder, "app_settings.json");
    public static string PasswordsFile => Path.Combine(DataFolder, "passwords.json");
    public static string RemindersFile => Path.Combine(DataFolder, "reminders.json");
    public static string WallpapersFile => Path.Combine(DataFolder, "wallpapers.json");
    public static string DynamicDataFile => Path.Combine(DataFolder, "dynamic_sources.json");
    public static string AiProvidersFile => Path.Combine(DataFolder, "ai_providers.json");
    public static string AiConversationsFile => Path.Combine(DataFolder, "ai_conversations.dat");
    public static string LogFile => Path.Combine(LogFolder, "newdesk.log");

    public static string WallpapersAssetsFolder => Path.Combine(DataFolder, "Wallpapers", "Assets");
    public static string DraftsFolder => Path.Combine(DataFolder, "Drafts");
    public static string BackupsFolder => Path.Combine(DataFolder, "Backups");
    public static string SecretsFolder => Path.Combine(DataFolder, "Secrets");
    public static string CacheFolder => Path.Combine(LogFolder, "Cache");

    public static void Initialize()
    {
        try
        {
            if (!Directory.Exists(DataFolder)) Directory.CreateDirectory(DataFolder);
            if (!Directory.Exists(LogFolder)) Directory.CreateDirectory(LogFolder);
            if (!Directory.Exists(WallpapersAssetsFolder)) Directory.CreateDirectory(WallpapersAssetsFolder);
            if (!Directory.Exists(DraftsFolder)) Directory.CreateDirectory(DraftsFolder);
            if (!Directory.Exists(BackupsFolder)) Directory.CreateDirectory(BackupsFolder);
            if (!Directory.Exists(SecretsFolder)) Directory.CreateDirectory(SecretsFolder);
            if (!Directory.Exists(CacheFolder)) Directory.CreateDirectory(CacheFolder);

            if (!AppEnvironment.IsTestMode)
            {
                // Automatic Migration from AppContext.BaseDirectory if needed
                MigrateFileIfNeeded("app_settings.json", SettingsFile);
                MigrateFileIfNeeded("passwords.json", PasswordsFile);
                MigrateFileIfNeeded("reminders.json", RemindersFile);
                MigrateFileIfNeeded("wallpapers.json", WallpapersFile);
            }
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

            // Implement simple Log Rotation (Max 5MB, keep 5 logs)
            RotateLogsIfNeeded();

            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(LogFile, entry);
        }
        catch
        {
            // Silent fallback
        }
    }

    private static void RotateLogsIfNeeded()
    {
        try
        {
            if (!File.Exists(LogFile)) return;
            var fi = new FileInfo(LogFile);
            if (fi.Length > 5 * 1024 * 1024) // 5MB limit
            {
                for (int i = 4; i >= 1; i--)
                {
                    string oldFile = Path.Combine(LogFolder, $"newdesk.{i}.log");
                    string newFile = Path.Combine(LogFolder, $"newdesk.{i + 1}.log");
                    if (File.Exists(oldFile))
                    {
                        if (File.Exists(newFile)) File.Delete(newFile);
                        File.Move(oldFile, newFile);
                    }
                }
                string firstBackup = Path.Combine(LogFolder, "newdesk.1.log");
                if (File.Exists(firstBackup)) File.Delete(firstBackup);
                File.Move(LogFile, firstBackup);
            }
        }
        catch
        {
            // Rotation fallback
        }
    }
}
