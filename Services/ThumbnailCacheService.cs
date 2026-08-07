using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NewDesk.Services;

public static class ThumbnailCacheService
{
    public static string GetOrCreateThumbnail(string imagePath, int maxPx = 256)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return string.Empty;

        try
        {
            string cacheFolder = Path.Combine(AppDataPath.CacheFolder, "Thumbnails");
            Directory.CreateDirectory(cacheFolder);

            string hash = GetMd5Hash(imagePath);
            string thumbPath = Path.Combine(cacheFolder, $"thumb_{hash}_{maxPx}.png");

            if (File.Exists(thumbPath)) return thumbPath;

            var frame = BitmapFrame.Create(new Uri(imagePath), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            double scale = (double)maxPx / Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (scale >= 1.0) return imagePath;

            int targetW = Math.Max(1, (int)(frame.PixelWidth * scale));
            int targetH = Math.Max(1, (int)(frame.PixelHeight * scale));

            var resized = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            var encoder = new PngBitmapEncoder { Frames = { BitmapFrame.Create(resized) } };

            using var fs = new FileStream(thumbPath, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);

            return thumbPath;
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("ThumbnailCacheService.GetOrCreateThumbnail", ex);
            return imagePath;
        }
    }

    private static string GetMd5Hash(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = MD5.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
