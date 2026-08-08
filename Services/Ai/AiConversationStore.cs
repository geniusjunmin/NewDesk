using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class AiConversationStore
{
    private static readonly object LockObj = new();

    public static List<AiConversation> LoadConversations()
    {
        lock (LockObj)
        {
            try
            {
                string path = AppDataPath.AiConversationsFile;
                if (!File.Exists(path)) return new List<AiConversation>();

                byte[] encryptedBytes = File.ReadAllBytes(path);
                if (encryptedBytes.Length == 0) return new List<AiConversation>();

                byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(decryptedBytes);
                return JsonSerializer.Deserialize<List<AiConversation>>(json) ?? new List<AiConversation>();
            }
            catch (Exception ex)
            {
                AppDataPath.LogError("AiConversationStore.LoadConversations", ex);
                return new List<AiConversation>();
            }
        }
    }

    public static void SaveConversations(List<AiConversation> conversations)
    {
        lock (LockObj)
        {
            try
            {
                string json = JsonSerializer.Serialize(conversations, new JsonSerializerOptions { WriteIndented = false });
                byte[] plainBytes = Encoding.UTF8.GetBytes(json);
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

                SafeFileWriter.WriteAllBytes(AppDataPath.AiConversationsFile, encryptedBytes);
            }
            catch (Exception ex)
            {
                AppDataPath.LogError("AiConversationStore.SaveConversations", ex);
                throw new IOException($"AI 对话保存失败: {ex.Message}", ex);
            }
        }
    }

    public static void ClearAll()
    {
        lock (LockObj)
        {
            try
            {
                if (File.Exists(AppDataPath.AiConversationsFile))
                {
                    File.Delete(AppDataPath.AiConversationsFile);
                }
            }
            catch (Exception ex)
            {
                AppDataPath.LogError("AiConversationStore.ClearAll", ex);
            }
        }
    }
}
