using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NewDesk.Models;
using NewDesk.Services;
using Xunit;

namespace NewDesk.Tests;

public sealed class BackupIntegrityTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskBackupIntegrity_{Guid.NewGuid():N}");

    public BackupIntegrityTests() => AppEnvironment.SetTestEnvironment(_testRoot);

    public void Dispose()
    {
        AppEnvironment.ResetToNormalEnvironment();
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [Fact]
    public void PortableBackup_ExcludesAiConversationDat()
    {
        AppDataPath.Initialize();
        File.WriteAllBytes(AppDataPath.AiConversationsFile, [1, 2, 3]);
        string zip = TempZip();
        BackupService.CreateBackup(zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("ai_conversations", StringComparison.OrdinalIgnoreCase));
        var manifest = ReadManifest(archive);
        Assert.Contains("ai_conversations.dat", manifest.ExcludedData);
    }

    [Fact]
    public void PortableBackup_ExcludesSecrets()
    {
        AppDataPath.Initialize();
        File.WriteAllText(Path.Combine(AppDataPath.SecretsFolder, "secret.dat"), "secret");
        string zip = TempZip();
        BackupService.CreateBackup(zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("Secrets/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Secrets/*", ReadManifest(archive).ExcludedData);
    }

    [Fact]
    public void ManifestDuplicatePathFails()
    {
        byte[] data = Encoding.UTF8.GetBytes("[]");
        var manifest = ManifestWith("data/reminders.json", data);
        manifest.Files.Add(new BackupFileEntry { FileName = "DATA/REMINDERS.JSON", Size = data.Length, Sha256 = Hash(data) });
        Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(CreateZip(manifest, ("data/reminders.json", data))));
    }

    [Fact]
    public void ManifestTraversalFails()
    {
        byte[] data = Encoding.UTF8.GetBytes("x");
        var manifest = ManifestWith("../escape", data);
        Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(CreateZip(manifest, ("data/reminders.json", data))));
    }

    [Fact]
    public void ManifestSizeMismatchFails()
    {
        byte[] data = Encoding.UTF8.GetBytes("[]");
        var manifest = ManifestWith("data/reminders.json", data);
        manifest.Files[0].Size++;
        Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(CreateZip(manifest, ("data/reminders.json", data))));
    }

    [Fact]
    public void ManifestHashMismatchFails()
    {
        byte[] data = Encoding.UTF8.GetBytes("[]");
        var manifest = ManifestWith("data/reminders.json", data);
        manifest.Files[0].Sha256 = new string('0', 64);
        Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(CreateZip(manifest, ("data/reminders.json", data))));
    }

    [Fact]
    public void UnsupportedBackupVersionFails()
    {
        var manifest = new BackupManifest { BackupVersion = "99.0" };
        var error = Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(CreateZip(manifest)));
        Assert.Contains("更新版本", error.Message);
    }

    [Fact]
    public void OldSchemaRestoreTriggersMigration()
    {
        Guid id = Guid.NewGuid();
        byte[] reminders = Encoding.UTF8.GetBytes($"[{{\"Id\":\"{id}\",\"Title\":\"生日\",\"Month\":8,\"Day\":10,\"IsLunar\":false}}]");
        var manifest = ManifestWith("data/reminders.json", reminders);
        manifest.SchemaVersions = new MigrationState { Reminders = 1 };

        Assert.True(BackupService.RestoreBackup(CreateZip(manifest, ("data/reminders.json", reminders))));

        Assert.Equal(3, MigrationService.LoadMigrationState().Reminders);
        Assert.Equal(ReminderScheduleType.Yearly, DataService.LoadReminders().Single().ScheduleType);
    }

    private string TempZip() => Path.Combine(_testRoot, $"{Guid.NewGuid():N}.zip");

    private string CreateZip(BackupManifest manifest, params (string Name, byte[] Data)[] files)
    {
        Directory.CreateDirectory(_testRoot);
        string zip = TempZip();
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(file.Name);
            using var stream = entry.Open();
            stream.Write(file.Data);
        }
        var manifestEntry = archive.CreateEntry("manifest.json");
        using (var writer = new StreamWriter(manifestEntry.Open())) writer.Write(JsonSerializer.Serialize(manifest));
        return zip;
    }

    private static BackupManifest ManifestWith(string fileName, byte[] data) => new()
    {
        Files = [new BackupFileEntry { FileName = fileName, Size = data.Length, Sha256 = Hash(data) }]
    };

    private static BackupManifest ReadManifest(ZipArchive archive)
    {
        using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        return JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd())!;
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
