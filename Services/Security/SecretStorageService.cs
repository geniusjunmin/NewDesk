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

    private static string SecretsFilePath => Path.Combine(AppDataPath.DataFolder, "Secrets", "ai_secrets.dat");

    public static void SaveSecret(string key, string secretValue)
    {
        if (string.IsNullOrEmpty(key)) return;

        lock (FileLock)
        {
            var secrets = LoadAllSecretsInternal();
            secrets[key] = secretValue;
            SaveAllSecretsInternal(secrets);
        }
    }

    public static string? GetSecret(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        lock (FileLock)
        {
            var secrets = LoadAllSecretsInternal();
            return secrets.TryGetValue(key, out var val) ? val : null;
        }
    }

    public static void DeleteSecret(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        lock (FileLock)
        {
            var secrets = LoadAllSecretsInternal();
            if (secrets.Remove(key))
            {
                SaveAllSecretsInternal(secrets);
            }
        }
    }

    private static Dictionary<string, string> LoadAllSecretsInternal()
    {
        try
        {
            string path = SecretsFilePath;
            if (!File.Exists(path)) return new Dictionary<string, string>();

            byte[] encryptedData = File.ReadAllBytes(path);
            byte[] decryptedData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(decryptedData);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static void SaveAllSecretsInternal(Dictionary<string, string> secrets)
    {
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
        }
    }
}
