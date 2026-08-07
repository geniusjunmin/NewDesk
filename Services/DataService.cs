using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using NewDesk.Models;

namespace NewDesk.Services;

public static class DataService
{
    private static readonly string PasswordsFilePath = Path.Combine(AppContext.BaseDirectory, "passwords.json");
    private static readonly string RemindersFilePath = Path.Combine(AppContext.BaseDirectory, "reminders.json");

    // In-memory storage for the master password for the current session.
    public static string? MasterPassword { get; set; }

    public static List<PasswordEntry> LoadPasswords()
    {
        if (string.IsNullOrEmpty(MasterPassword) || !File.Exists(PasswordsFilePath))
        {
            return new List<PasswordEntry>();
        }

        string json = File.ReadAllText(PasswordsFilePath);
        var payload = JsonSerializer.Deserialize<EncryptedPayload>(json);
        if (payload == null)
        {
            return new List<PasswordEntry>(); // Or handle error
        }

        try
        {
            string decryptedJson = CryptographyService.Decrypt(payload, MasterPassword);
            return JsonSerializer.Deserialize<List<PasswordEntry>>(decryptedJson) ?? new List<PasswordEntry>();
        }
        catch (JsonException)
        {
            // This can happen if the file is corrupted or not valid JSON.
            return new List<PasswordEntry>();
        }
        catch (CryptographicException)
        {
            // This will be thrown if the master password is incorrect.
            throw; // Re-throw to be caught by the UI logic
        }
    }

    public static void SavePasswords(IEnumerable<PasswordEntry> passwords)
    { 
        if (string.IsNullOrEmpty(MasterPassword))
        {
            throw new InvalidOperationException("Master password is not set.");
        }

        string json = JsonSerializer.Serialize(passwords, new JsonSerializerOptions { WriteIndented = false });
        var payload = CryptographyService.Encrypt(json, MasterPassword);
        string payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(PasswordsFilePath, payloadJson);
    }

    public static List<Reminder> LoadReminders()
    {
        if (!File.Exists(RemindersFilePath))
        {
            return new List<Reminder>();
        }

        string json = File.ReadAllText(RemindersFilePath);
        return JsonSerializer.Deserialize<List<Reminder>>(json) ?? new List<Reminder>();
    }

    public static void SaveReminders(IEnumerable<Reminder> reminders)
    {
        string json = JsonSerializer.Serialize(reminders, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(RemindersFilePath, json);
    }
    
    public static bool PasswordFileExists() => File.Exists(PasswordsFilePath);

    public static int ImportLegacyPasswords()
    {
        string legacyFilePath = Path.Combine(AppContext.BaseDirectory, "allpaw");
        if (!File.Exists(legacyFilePath))
        {
             return -1; // File not found
        }

        string alltext = File.ReadAllText(legacyFilePath, System.Text.Encoding.UTF8);
        if (string.IsNullOrEmpty(alltext))
        {
            return 0; // Empty file
        }

        // Decrypt using legacy logic (Crypto class already exists in project)
        string decryptedText = Crypto.DESDecrypt(alltext, "512", "512");
        if (string.IsNullOrEmpty(decryptedText))
        {
            return 0; // Decryption failed or empty
        }

        string[] entries = decryptedText.Split('|');
        var existingPasswords = LoadPasswords();
        int importCount = 0;

        foreach (string entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;

            string[] parts = entry.Split('㊣');
            if (parts.Length < 2) continue;

            string title = parts[0];
            string password = parts[1];

            if (string.IsNullOrEmpty(title)) continue;

            // Check if already exists by title (simple merge logic)
            bool exists = false;
            foreach (var existing in existingPasswords)
            {
                if (existing.Title == title)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                existingPasswords.Add(new PasswordEntry
                {
                    Id = Guid.NewGuid(),
                    Title = title,
                    Password = password,
                    Username = "", // Legacy format doesn't seem to have username separately, or it's part of title
                    Notes = "Imported from allpaw"
                });
                importCount++;
            }
        }

        if (importCount > 0)
        {
            SavePasswords(existingPasswords);
        }

        return importCount;
    }
}
