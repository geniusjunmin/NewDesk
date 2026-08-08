using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace NewDesk.Services;

public class BackupManifest
{
    public string BackupVersion { get; set; } = "2.2.0";
    public string AppVersion { get; set; } = "2.2.0";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<string> IncludedFiles { get; set; } = new();
}

public static class BackupService
{
    public static void CreateBackup(string targetZipPath)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"NewDeskBackup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string dataDir = Path.Combine(tempDir, "data");
            Directory.CreateDirectory(dataDir);

            var manifest = new BackupManifest
            {
                AppVersion = AppVersionService.Version,
                CreatedAt = DateTime.Now
            };

            string[] filesToBackup = new[]
            {
                AppDataPath.SettingsFile,
                AppDataPath.PasswordsFile,
                AppDataPath.RemindersFile,
                AppDataPath.WallpapersFile,
                AppDataPath.DynamicDataFile,
                AppDataPath.AiProvidersFile
            };

            foreach (var filePath in filesToBackup)
            {
                if (File.Exists(filePath))
                {
                    string fileName = Path.GetFileName(filePath);
                    File.Copy(filePath, Path.Combine(dataDir, fileName), true);
                    manifest.IncludedFiles.Add(fileName);
                }
            }

            string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(tempDir, "manifest.json"), manifestJson);

            if (File.Exists(targetZipPath))
            {
                File.Delete(targetZipPath);
            }

            ZipFile.CreateFromDirectory(tempDir, targetZipPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    public static bool RestoreBackup(string sourceZipPath)
    {
        if (!File.Exists(sourceZipPath)) return false;

        string tempDir = Path.Combine(Path.GetTempPath(), $"NewDeskRestore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Auto backup before restore
            string preRestoreFolder = Path.Combine(AppDataPath.DataFolder, "Backups", "BeforeRestore");
            Directory.CreateDirectory(preRestoreFolder);
            string autoBackupPath = Path.Combine(preRestoreFolder, $"PreRestore_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            CreateBackup(autoBackupPath);

            ZipFile.ExtractToDirectory(sourceZipPath, tempDir);

            string manifestPath = Path.Combine(tempDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("备份压缩包中缺少 manifest.json 校验元数据文件。");
            }

            string dataDir = Path.Combine(tempDir, "data");
            if (Directory.Exists(dataDir))
            {
                RestoreDataFileIfExists(dataDir, "app_settings.json", AppDataPath.SettingsFile);
                RestoreDataFileIfExists(dataDir, "passwords.json", AppDataPath.PasswordsFile);
                RestoreDataFileIfExists(dataDir, "reminders.json", AppDataPath.RemindersFile);
                RestoreDataFileIfExists(dataDir, "wallpapers.json", AppDataPath.WallpapersFile);
                RestoreDataFileIfExists(dataDir, "dynamic_sources.json", AppDataPath.DynamicDataFile);
                RestoreDataFileIfExists(dataDir, "ai_providers.json", AppDataPath.AiProvidersFile);
            }

            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    private static void RestoreDataFileIfExists(string sourceDir, string fileName, string targetPath)
    {
        string srcPath = Path.Combine(sourceDir, fileName);
        if (File.Exists(srcPath))
        {
            string content = File.ReadAllText(srcPath);
            SafeFileWriter.WriteAllText(targetPath, content);
        }
    }
}
