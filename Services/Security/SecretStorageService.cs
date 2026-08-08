using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NewDesk.Services.Security;

public static class SecretStorageService
{
    private static readonly object FileLock = new();
    private static bool _isCorruptedState;

    private static string SecretsFilePath => Path.Combine(AppDataPath.DataFolder, "Secrets", "ai_secrets.dat");

    public static void SaveSecret(string key, string secretValue)
    {
        if (string.IsNullOrEmpty(key)) return;

        lock (FileLock)
        {
            var secrets = LoadAllSecretsInternal();
            if (_isCorruptedState)
            {
                throw new InvalidOperationException("密钥存储文件已损坏，已暂停覆盖保存以保护数据。已自动备份损坏文件。");
            }
            secrets[key] = secretValue;
            SaveAllSecretsInternal(secrets);
        }
    }

    public static string? GetSecret(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        lock (FileLock)
        {
            var secrets = LoadAllSecretsInternal();
            return secrets.TryGetValue(key, out var val) ? val : null;
        }
    }

    public static void DeleteSecret(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;

        lock (FileLock)
        {
            var secrets = LoadAllSecretsInternal();
            if (_isCorruptedState)
            {
                throw new InvalidOperationException("密钥存储文件已损坏，已暂停修改操作。");
            }
            if (secrets.Remove(key))
            {
                SaveAllSecretsInternal(secrets);
            }
        }
    }

    private static Dictionary<string, string> LoadAllSecretsInternal()
    {
        string path = SecretsFilePath;
        if (!File.Exists(path))
        {
            _isCorruptedState = false;
            return new Dictionary<string, string>();
        }

        try
        {
            byte[] encryptedData = File.ReadAllBytes(path);
            if (encryptedData.Length == 0)
            {
                _isCorruptedState = false;
                return new Dictionary<string, string>();
            }

            byte[] decryptedData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(decryptedData);

            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            _isCorruptedState = false;
            return result ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _isCorruptedState = true;
            AppDataPath.LogError("SecretStorageService.LoadAllSecretsInternal (CORRUPTED DPAPI FILE)", ex);

            try
            {
                string corruptBackupPath = Path.Combine(Path.GetDirectoryName(path)!, $"ai_secrets.corrupt.{DateTime.Now:yyyyMMdd_HHmmss}.dat");
                File.Copy(path, corruptBackupPath, true);
                AppDataPath.LogInfo($"Copied corrupted secrets file to: {corruptBackupPath}");
            }
            catch { }

            return new Dictionary<string, string>();
        }
    }

    private static void SaveAllSecretsInternal(Dictionary<string, string> secrets)
    {
        if (_isCorruptedState)
        {
            throw new InvalidOperationException("无法覆盖保存损坏的密钥数据库。");
        }

        try
        {
            string dir = Path.GetDirectoryName(SecretsFilePath)!;
            Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(secrets);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            SafeFileWriter.WriteAllBytes(SecretsFilePath, encryptedBytes);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("SecretStorageService.SaveAllSecretsInternal", ex);
            throw;
        }
    }
}
