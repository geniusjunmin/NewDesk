using System;
using System.IO;
using System.Text;

namespace NewDesk.Services;

public static class SafeFileWriter
{
    public static void WriteAllText(string filePath, string content)
    {
        string directory = Path.GetDirectoryName(filePath)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempFilePath = filePath + ".tmp";
        try
        {
            using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                writer.Write(content);
                writer.Flush();
                fs.Flush(true);
            }

            if (File.Exists(filePath))
            {
                File.Replace(tempFilePath, filePath, null);
            }
            else
            {
                File.Move(tempFilePath, filePath);
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError($"SafeFileWriter.WriteAllText failed for ({filePath})", ex);
            throw new IOException($"SafeFileWriter 原子文件写入失败 ({filePath}): {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
        }
    }

    public static void WriteAllBytes(string filePath, byte[] data)
    {
        string directory = Path.GetDirectoryName(filePath)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempFilePath = filePath + ".tmp";
        try
        {
            using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }

            if (File.Exists(filePath))
            {
                File.Replace(tempFilePath, filePath, null);
            }
            else
            {
                File.Move(tempFilePath, filePath);
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError($"SafeFileWriter.WriteAllBytes failed for ({filePath})", ex);
            throw new IOException($"SafeFileWriter 原子二进制写入失败 ({filePath}): {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
        }
    }
}
