using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using NewDesk.Models;

namespace NewDesk.Services;

public static class DataService
{
    public static string PasswordsFilePath => File.Exists(AppDataPath.PasswordsFile)
        ? AppDataPath.PasswordsFile
        : (File.Exists(Path.Combine(AppContext.BaseDirectory, "passwords.json"))
            ? Path.Combine(AppContext.BaseDirectory, "passwords.json")
            : AppDataPath.PasswordsFile);

    public static string RemindersFilePath => File.Exists(AppDataPath.RemindersFile)
        ? AppDataPath.RemindersFile
        : (File.Exists(Path.Combine(AppContext.BaseDirectory, "reminders.json"))
            ? Path.Combine(AppContext.BaseDirectory, "reminders.json")
            : AppDataPath.RemindersFile);

    // In-memory storage for the master password for the current session.
    public static string? MasterPassword { get; set; }

    public static List<PasswordEntry> LoadPasswords()
    {
        AppDataPath.Initialize();
        string path = PasswordsFilePath;

        if (string.IsNullOrEmpty(MasterPassword) || !File.Exists(path))
        {
            return new List<PasswordEntry>();
        }

        string json = File.ReadAllText(path);
        var payload = JsonSerializer.Deserialize<EncryptedPayload>(json);
        if (payload == null)
        {
            return new List<PasswordEntry>();
        }

        try
        {
            string decryptedJson = CryptographyService.Decrypt(payload, MasterPassword);
            return JsonSerializer.Deserialize<List<PasswordEntry>>(decryptedJson) ?? new List<PasswordEntry>();
        }
        catch (JsonException ex)
        {
            AppDataPath.LogError("DataService.LoadPasswords (JsonException)", ex);
            return new List<PasswordEntry>();
        }
        catch (CryptographicException)
        {
            throw; // Re-throw to be caught by the UI logic for incorrect password
        }
    }

    public static void SavePasswords(IEnumerable<PasswordEntry> passwords)
    { 
        AppDataPath.Initialize();
        if (string.IsNullOrEmpty(MasterPassword))
        {
            throw new InvalidOperationException("主密码未设置。");
        }

        string json = JsonSerializer.Serialize(passwords, new JsonSerializerOptions { WriteIndented = false });
        var payload = CryptographyService.Encrypt(json, MasterPassword);
        string payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        SafeFileWriter.WriteAllText(AppDataPath.PasswordsFile, payloadJson);
    }

    public static List<Reminder> LoadReminders()
    {
        AppDataPath.Initialize();
        string path = RemindersFilePath;

        if (!File.Exists(path))
        {
            return new List<Reminder>();
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<Reminder>>(json) ?? new List<Reminder>();
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("DataService.LoadReminders", ex);
            return new List<Reminder>();
        }
    }

    public static void SaveReminders(IEnumerable<Reminder> reminders)
    {
        AppDataPath.Initialize();
        string json = JsonSerializer.Serialize(reminders, new JsonSerializerOptions { WriteIndented = true });
        SafeFileWriter.WriteAllText(AppDataPath.RemindersFile, json);
    }
    
    public static bool PasswordFileExists() => File.Exists(PasswordsFilePath);

    public static bool AnyDataExists()
    {
        return PasswordFileExists() || File.Exists(RemindersFilePath) || File.Exists(AppDataPath.WallpapersFile);
    }

    public static int ImportLegacyPasswords()
    {
        string legacyFilePath = File.Exists(Path.Combine(AppDataPath.DataFolder, "allpaw"))
            ? Path.Combine(AppDataPath.DataFolder, "allpaw")
            : Path.Combine(AppContext.BaseDirectory, "allpaw");

        if (!File.Exists(legacyFilePath))
        {
            return -1; // File not found
        }

        try
        {
            string alltext = File.ReadAllText(legacyFilePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrEmpty(alltext))
            {
                return 0; // Empty file
            }

            string decryptedText = Crypto.DESDecrypt(alltext, "512", "512");
            if (string.IsNullOrEmpty(decryptedText))
            {
                return 0; // Decryption failed or empty
            }

            string[] entries = decryptedText.Split('|');
            var importedList = new List<PasswordEntry>();

            foreach (string entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;

                string[] parts = entry.Split('㊣');
                if (parts.Length < 2) continue;

                string title = parts[0];
                string password = parts[1];

                if (string.IsNullOrEmpty(title)) continue;

                importedList.Add(new PasswordEntry
                {
                    Id = Guid.NewGuid(),
                    Title = title,
                    Password = password,
                    Username = "",
                    Notes = "从 legacy allpaw 自动恢复"
                });
            }

            if (importedList.Count > 0 && !string.IsNullOrEmpty(MasterPassword))
            {
                SavePasswords(importedList);
            }

            return importedList.Count;
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("ImportLegacyPasswords", ex);
            return 0;
        }
    }
}
