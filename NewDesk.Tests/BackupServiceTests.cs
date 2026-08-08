using System;
using System.IO;
using System.IO.Compression;
using NewDesk.Services;
using Xunit;

namespace NewDesk.Tests;

public class BackupServiceTests
{
    [Fact]
    public void RestoreBackup_RejectsPathTraversalZip()
    {
        string tempZip = Path.Combine(Path.GetTempPath(), $"malicious_test_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../evil.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("malicious content");
            }

            var ex = Assert.Throws<InvalidOperationException>(() => BackupService.RestoreBackup(tempZip));
            Assert.Contains("非法路径", ex.Message);
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }
}
