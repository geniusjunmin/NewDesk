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
                fs.Flush(true); // Ensure disk flush
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
            AppDataPath.LogError($"SafeFileWriter.WriteAllText ({filePath})", ex);
            // Fallback direct write if replace fails
            File.WriteAllText(filePath, content, Encoding.UTF8);
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
