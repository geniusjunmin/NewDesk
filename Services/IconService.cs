using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NewDesk.Services
{
    public static class IconService
    {
        private const string IconRelativePath = "Resources/app_icon.png";
        
        public static void EnsureIconExists()
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, IconRelativePath);
            string? directory = Path.GetDirectoryName(fullPath);
            
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            if (!File.Exists(fullPath))
            {
                GenerateIcon(fullPath);
            }
        }
        
        public static ImageSource GetIconSource()
        {
            EnsureIconExists();
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, IconRelativePath);
            return new BitmapImage(new Uri(fullPath));
        }

        private static void GenerateIcon(string outputPath)
        {
            int size = 256;
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // 1. Background (Rounded Rectangle with Gradient)
                var gradient = new LinearGradientBrush(
                    Color.FromRgb(74, 144, 226), // PrimaryColor (#4A90E2)
                    Color.FromRgb(53, 122, 189), // PrimaryDarkColor (#357ABD)
                    new Point(0, 0), new Point(1, 1));
                
                dc.DrawRoundedRectangle(gradient, null, new Rect(0, 0, size, size), 40, 40);

                // 2. Subtle Gloss effect
                var glossGradient = new LinearGradientBrush(
                    Color.FromArgb(50, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0), new Point(0, 0.5));
                dc.DrawRoundedRectangle(glossGradient, null, new Rect(10, 10, size - 20, size / 2.0), 30, 30);

                // 3. Stylized 'D' or Desktop Icon
                // We'll draw a "Desktop" symbol: A monitor-like shape
                Pen whitePen = new Pen(Brushes.White, 12);
                whitePen.StartLineCap = PenLineCap.Round;
                whitePen.EndLineCap = PenLineCap.Round;
                whitePen.LineJoin = PenLineJoin.Round;

                // Monitor frame
                double margin = 60;
                double frameWidth = size - (margin * 2);
                double frameHeight = frameWidth * 0.7;
                Rect monitorRect = new Rect(margin, margin + 20, frameWidth, frameHeight);
                dc.DrawRoundedRectangle(null, whitePen, monitorRect, 10, 10);

                // Monitor stand
                dc.DrawLine(whitePen, 
                    new Point(size / 2.0, monitorRect.Bottom), 
                    new Point(size / 2.0, monitorRect.Bottom + 25));
                dc.DrawLine(whitePen, 
                    new Point(size / 2.0 - 30, monitorRect.Bottom + 25), 
                    new Point(size / 2.0 + 30, monitorRect.Bottom + 25));

                // A small dot representing a power button or logo
                dc.DrawEllipse(Brushes.White, null, new Point(size / 2.0, monitorRect.Bottom - 15), 5, 5);
            }

            RenderTargetBitmap bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));

            using (Stream stream = File.Create(outputPath))
            {
                encoder.Save(stream);
            }
        }
    }
}
