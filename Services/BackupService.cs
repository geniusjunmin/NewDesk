using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NewDesk.Services;

public class BackupFileEntry
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public class BackupManifest
{
    public string BackupVersion { get; set; } = "2.2.1";
    public string AppVersion { get; set; } = "2.2.1";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<BackupFileEntry> Files { get; set; } = new();
}

public static class BackupService
{
    public static void CreateBackup(string targetZipPath)
    {
        string stagingDir = Path.Combine(Path.GetTempPath(), $"NewDeskBackupStaging_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        try
        {
            string dataDir = Path.Combine(stagingDir, "data");
            Directory.CreateDirectory(dataDir);

            string assetsDir = Path.Combine(stagingDir, "assets");
            Directory.CreateDirectory(assetsDir);

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
                    string destPath = Path.Combine(dataDir, fileName);
                    File.Copy(filePath, destPath, true);

                    byte[] data = File.ReadAllBytes(destPath);
                    manifest.Files.Add(new BackupFileEntry
                    {
                        FileName = "data/" + fileName,
                        Size = data.Length,
                        Sha256 = ComputeSha256(data)
                    });
                }
            }

            // Backup Wallpaper Assets if present
            string localAssetsFolder = Path.Combine(AppDataPath.DataFolder, "Wallpapers", "Assets");
            if (Directory.Exists(localAssetsFolder))
            {
                foreach (var assetFile in Directory.GetFiles(localAssetsFolder))
                {
                    string assetName = Path.GetFileName(assetFile);
                    string destAsset = Path.Combine(assetsDir, assetName);
                    File.Copy(assetFile, destAsset, true);

                    byte[] data = File.ReadAllBytes(destAsset);
                    manifest.Files.Add(new BackupFileEntry
                    {
                        FileName = "assets/" + assetName,
                        Size = data.Length,
                        Sha256 = ComputeSha256(data)
                    });
                }
            }

            string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(stagingDir, "manifest.json"), manifestJson);

            if (File.Exists(targetZipPath))
            {
                File.Delete(targetZipPath);
            }

            ZipFile.CreateFromDirectory(stagingDir, targetZipPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
            }
            catch { }
        }
    }

    public static bool RestoreBackup(string sourceZipPath)
    {
        if (!File.Exists(sourceZipPath)) return false;

        // 1. Inspect ZIP entries for path traversal before extracting
        using (var archive = ZipFile.OpenRead(sourceZipPath))
        {
            foreach (var entry in archive.Entries)
            {
                string name = entry.FullName;
                if (name.Contains("..") || name.StartsWith("/") || name.StartsWith("\\") || name.Contains(":"))
                {
                    throw new InvalidOperationException($"备份包包含非法路径与目录遍历风险 ({name})。解压已被阻止。");
                }
            }
        }

        string stagingDir = Path.Combine(Path.GetTempPath(), $"NewDeskRestoreStaging_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        try
        {
            // 2. Pre-restore auto backup
            string preRestoreFolder = Path.Combine(AppDataPath.DataFolder, "Backups", "BeforeRestore");
            Directory.CreateDirectory(preRestoreFolder);
            string autoBackupPath = Path.Combine(preRestoreFolder, $"PreRestore_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            CreateBackup(autoBackupPath);

            // 3. Extract to staging directory for SHA256 validation
            ZipFile.ExtractToDirectory(sourceZipPath, stagingDir);

            string manifestPath = Path.Combine(stagingDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("备份包中缺少 manifest.json 校验元数据。");
            }

            string manifestJson = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson);
            if (manifest == null)
            {
                throw new InvalidOperationException("manifest.json 解析失败。");
            }

            // 4. Validate SHA256 hashes of staged files against manifest
            foreach (var entry in manifest.Files)
            {
                string stagedFilePath = Path.Combine(stagingDir, entry.FileName.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(stagedFilePath))
                {
                    byte[] data = File.ReadAllBytes(stagedFilePath);
                    string hash = ComputeSha256(data);
                    if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"文件哈希校验失败 ({entry.FileName})。备份文件可能已被篡改。");
                    }
                }
            }

            // 5. Atomic restore into real AppData
            string dataDir = Path.Combine(stagingDir, "data");
            if (Directory.Exists(dataDir))
            {
                RestoreDataFileIfExists(dataDir, "app_settings.json", AppDataPath.SettingsFile);
                RestoreDataFileIfExists(dataDir, "passwords.json", AppDataPath.PasswordsFile);
                RestoreDataFileIfExists(dataDir, "reminders.json", AppDataPath.RemindersFile);
                RestoreDataFileIfExists(dataDir, "wallpapers.json", AppDataPath.WallpapersFile);
                RestoreDataFileIfExists(dataDir, "dynamic_sources.json", AppDataPath.DynamicDataFile);
                RestoreDataFileIfExists(dataDir, "ai_providers.json", AppDataPath.AiProvidersFile);
            }

            string assetsDir = Path.Combine(stagingDir, "assets");
            if (Directory.Exists(assetsDir))
            {
                string localAssetsFolder = Path.Combine(AppDataPath.DataFolder, "Wallpapers", "Assets");
                Directory.CreateDirectory(localAssetsFolder);
                foreach (var assetFile in Directory.GetFiles(assetsDir))
                {
                    string dest = Path.Combine(localAssetsFolder, Path.GetFileName(assetFile));
                    byte[] data = File.ReadAllBytes(assetFile);
                    SafeFileWriter.WriteAllBytes(dest, data);
                }
            }

            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
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

    private static string ComputeSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash);
    }
}
