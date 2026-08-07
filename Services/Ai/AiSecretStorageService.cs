using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NewDesk.Services.Ai;

public static class AiSecretStorageService
{
    private static readonly object LockObj = new();

    private static string SecretsFilePath => Path.Combine(AppDataPath.SecretsFolder, "ai_secrets.dat");

    public static void SaveSecret(string secretId, string secretValue)
    {
        if (string.IsNullOrEmpty(secretId)) return;

        lock (LockObj)
        {
            var secrets = LoadAllSecrets();
            secrets[secretId] = secretValue;
            SaveAllSecrets(secrets);
        }
    }

    public static string? GetSecret(string? secretId)
    {
        if (string.IsNullOrEmpty(secretId)) return null;

        lock (LockObj)
        {
            var secrets = LoadAllSecrets();
            return secrets.TryGetValue(secretId, out var val) ? val : null;
        }
    }

    public static void DeleteSecret(string secretId)
    {
        if (string.IsNullOrEmpty(secretId)) return;

        lock (LockObj)
        {
            var secrets = LoadAllSecrets();
            if (secrets.Remove(secretId))
            {
                SaveAllSecrets(secrets);
            }
        }
    }

    private static Dictionary<string, string> LoadAllSecrets()
    {
        try
        {
            string path = SecretsFilePath;
            if (!File.Exists(path)) return new Dictionary<string, string>();

            byte[] encryptedBytes = File.ReadAllBytes(path);
            if (encryptedBytes.Length == 0) return new Dictionary<string, string>();

            byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(decryptedBytes);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("AiSecretStorageService.LoadAllSecrets", ex);
            return new Dictionary<string, string>();
        }
    }

    private static void SaveAllSecrets(Dictionary<string, string> secrets)
    {
        try
        {
            string json = JsonSerializer.Serialize(secrets);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            string directory = Path.GetDirectoryName(SecretsFilePath)!;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            SafeFileWriter.WriteAllText(SecretsFilePath + ".enc", Convert.ToBase64String(encryptedBytes));
            File.WriteAllBytes(SecretsFilePath, encryptedBytes);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("AiSecretStorageService.SaveAllSecrets", ex);
        }
    }
}
