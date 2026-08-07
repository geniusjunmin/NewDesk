using System;
using System.IO;
using System.Text.Json;

namespace NewDesk.Services;

public static class MigrationService
{
    public const int CurrentSettingsSchemaVersion = 2;
    public const int CurrentPasswordSchemaVersion = 2;
    public const int CurrentReminderSchemaVersion = 2;
    public const int CurrentWallpaperSchemaVersion = 2;
    public const int CurrentDynamicDataSchemaVersion = 2;
    public const int CurrentAiSchemaVersion = 1;

    public static void RunAllMigrationsIfNeeded()
    {
        try
        {
            AppDataPath.Initialize();
            string migrationBackupDir = Path.Combine(AppDataPath.BackupsFolder, "Migration");
            Directory.CreateDirectory(migrationBackupDir);

            BackupAndMigrateFile(AppDataPath.WallpapersFile, "wallpapers", migrationBackupDir);
            BackupAndMigrateFile(AppDataPath.RemindersFile, "reminders", migrationBackupDir);
            BackupAndMigrateFile(AppDataPath.PasswordsFile, "passwords", migrationBackupDir);
            BackupAndMigrateFile(AppDataPath.SettingsFile, "settings", migrationBackupDir);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("MigrationService.RunAllMigrationsIfNeeded", ex);
        }
    }

    private static void BackupAndMigrateFile(string filePath, string fileTag, string backupDir)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            string timeStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupPath = Path.Combine(backupDir, $"{fileTag}.{timeStamp}.backup.json");
            File.Copy(filePath, backupPath, overwrite: true);
            AppDataPath.LogInfo($"Created pre-migration backup for {fileTag} at {backupPath}");
        }
        catch (Exception ex)
        {
            AppDataPath.LogError($"MigrationService.Backup ({fileTag})", ex);
        }
    }
}
