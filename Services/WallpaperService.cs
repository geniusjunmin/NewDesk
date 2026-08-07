using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NewDesk.Models;

namespace NewDesk.Services;

public static class WallpaperService
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 20;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDWININICHANGE = 0x02;
    
    private static readonly HttpClient HttpClient = new();
    private static DispatcherTimer? _refreshTimer;
    private static DispatcherTimer? _rotationTimer;
    private static WallpaperState? _currentState;
    private static List<WallpaperState> _rotationList = new();
    private static int _currentRotationIndex = -1;
    private static WallpaperRotationMode _rotationMode;
    private static bool _isDisplayListenerInitialized = false;

    public static void InitializeDisplaySettingsListener()
    {
        if (_isDisplayListenerInitialized) return;
        
        SystemEvents.DisplaySettingsChanged += async (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine("Display settings changed. Refreshing wallpaper after delay...");
            // Delay a bit to let the system stabilize
            await Task.Delay(2000);
            await RefreshAsync();
        };
        
        _isDisplayListenerInitialized = true;
    }

    public static void StartAutoRefresh(WallpaperState state)
    {
        StopAutoRefresh();
        _currentState = state;

        // Auto-refresh for dynamic content (time, API, etc.)
        if (state.RefreshIntervalMinutes > 0)
        {
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromMinutes(state.RefreshIntervalMinutes);
            _refreshTimer.Tick += async (s, e) => await RefreshAsync();
            _refreshTimer.Start();
        }
    }

    public static void StopAutoRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        // Do NOT null out _currentState here if we are just stopping the timer but keeping the wallpaper
        // But for clarity, let's keep it as is, assuming this stops *everything* about that specific wallpaper instance.
        _currentState = null;
    }

    public static void StartRotation(List<WallpaperState> wallpapers, int intervalMinutes, WallpaperRotationMode mode)
    {
        StopRotation();
        if (wallpapers == null || wallpapers.Count == 0) return;

        _rotationList = wallpapers.ToList(); // Clone list to be safe
        _rotationMode = mode;
        _currentRotationIndex = -1;

        _rotationTimer = new DispatcherTimer();
        _rotationTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));
        _rotationTimer.Tick += async (s, e) => await RotateNextAsync();
        _rotationTimer.Start();

        // Trigger first rotation immediately on the UI thread
        _ = Application.Current?.Dispatcher?.InvokeAsync(RotateNextAsync);
    }

    public static void StopRotation()
    {
        _rotationTimer?.Stop();
        _rotationTimer = null;
        _rotationList.Clear();
    }

    public static async Task RotateNextAsync()
    {
        try
        {
            if (_rotationList == null || _rotationList.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("Rotation failed: List is empty.");
                return;
            }

            if (_rotationMode == WallpaperRotationMode.Random)
            {
                var random = new Random();
                _currentRotationIndex = random.Next(_rotationList.Count);
            }
            else // Sequential
            {
                _currentRotationIndex++;
                if (_currentRotationIndex >= _rotationList.Count || _currentRotationIndex < 0) 
                    _currentRotationIndex = 0;
            }

            if (_currentRotationIndex >= 0 && _currentRotationIndex < _rotationList.Count)
            {
                var nextState = _rotationList[_currentRotationIndex];
                
                System.Diagnostics.Debug.WriteLine($"[Rotation] Switching to: {nextState.Name} (Index: {_currentRotationIndex})");
                
                // Set wallpaper (this handles generation)
                await GenerateAndSetWallpaperAsync(nextState);
                
                // Start auto-refresh for dynamic content within this wallpaper
                StartAutoRefresh(nextState);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rotation error: {ex.Message}");
            // Use dispatcher to show toast since we might be on a timer thread
            Application.Current?.Dispatcher?.Invoke(() => 
                ToastManager.Show("轮换错误", $"自动更换壁纸失败: {ex.Message}", ToastType.Error));
        }
    }

    public static async Task RefreshAsync()
    {
        if (_currentState == null) return;
        
        try
        {
            await GenerateAndSetWallpaperAsync(_currentState);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Auto-refresh failed: {ex.Message}");
        }
    }

    public static async Task GenerateAndSetWallpaperAsync(WallpaperState state)
    {
        string logPath = @"D:\wallpaper_debug.txt";
        File.AppendAllText(logPath, $"[{DateTime.Now}] Starting GenerateAndSetWallpaperAsync\n");

        // 1. Fetch API Data
        var apiTasks = new Dictionary<TextElementState, Task<string>>();
        foreach (var element in state.TextElements.Where(e => e.DynamicType == "Api" && !string.IsNullOrEmpty(e.ApiUrl)))
        {
            apiTasks[element] = GetApiTextAsync(element.ApiUrl!, element.ApiRegex, element.ApiFormatting, element.ApiPrefix, element.ApiSuffix);
        }
        await Task.WhenAll(apiTasks.Values);
        var apiResults = apiTasks.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Result);

        // 2. Render and Set
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var desktopWallpaper = (NativeMethods.IDesktopWallpaper)new NativeMethods.DesktopWallpaperClass();
                desktopWallpaper.Enable(true);
                uint monitorCount = desktopWallpaper.GetMonitorDevicePathCount();
                File.AppendAllText(logPath, $"Detected {monitorCount} monitors via IDesktopWallpaper.\n");
                
                if (monitorCount == 0) throw new Exception("No monitors detected.");

                bool anySuccess = false;
                for (uint i = 0; i < monitorCount; i++)
                {
                    string monitorId = desktopWallpaper.GetMonitorDevicePathAt(i);
                    File.AppendAllText(logPath, $"Monitor {i} ID: {monitorId}\n");
                    
                    if (string.IsNullOrEmpty(monitorId)) continue;
                    
                    desktopWallpaper.GetMonitorRECT(monitorId, out NativeMethods.RECT rect);
                    File.AppendAllText(logPath, $"Monitor {i} RECT: L={rect.Left}, T={rect.Top}, R={rect.Right}, B={rect.Bottom}\n");

                    int pixelWidth = Math.Abs(rect.Right - rect.Left);
                    int pixelHeight = Math.Abs(rect.Bottom - rect.Top);
                    File.AppendAllText(logPath, $"Monitor {i} Size: {pixelWidth}x{pixelHeight}\n");

                    if (pixelWidth <= 0 || pixelHeight <= 0) continue;

                    var drawingVisual = new DrawingVisual();
                    RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.HighQuality);

                    using (var dc = drawingVisual.RenderOpen())
                    {
                        if (!string.IsNullOrEmpty(state.BackgroundImagePath) && File.Exists(state.BackgroundImagePath))
                        {
                            var originalImage = BitmapFrame.Create(new Uri(state.BackgroundImagePath), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                            dc.DrawRectangle(new ImageBrush(originalImage) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center }, null, new Rect(0, 0, pixelWidth, pixelHeight));
                        }
                        else dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, pixelWidth, pixelHeight));

                        double scaleX = state.DesignWidth > 0 ? (double)pixelWidth / state.DesignWidth : 1.0;
                        double scaleY = state.DesignHeight > 0 ? (double)pixelHeight / state.DesignHeight : 1.0;
                        double fontScale = Math.Min(scaleX, scaleY);

                        foreach (var element in state.TextElements)
                        {
                            string apiResultText = apiResults.TryGetValue(element, out var res) ? res : string.Empty;
                            DrawTextElementOnDrawingContext(dc, element, apiResultText, scaleX, scaleY);
                        }
                    }

                    var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(drawingVisual);
                    var encoder = new PngBitmapEncoder { Frames = { BitmapFrame.Create(rtb) } };
                    string cacheDir = Path.Combine(Path.GetTempPath(), "NewDesk", "Wallpapers");
                    Directory.CreateDirectory(cacheDir);
                    string wallpaperPath = Path.Combine(cacheDir, $"wallpaper_{i}.png");
                    using (var fs = new FileStream(wallpaperPath, FileMode.Create, FileAccess.Write)) encoder.Save(fs);
                    
                    try
                    {
                        desktopWallpaper.SetWallpaper(monitorId, wallpaperPath);
                        File.AppendAllText(logPath, $"Set wallpaper for monitor {i} success.\n");
                        anySuccess = true;
                    }
                    catch (Exception ex) 
                    { 
                        File.AppendAllText(logPath, $"Failed to set wallpaper for monitor {i}: {ex.Message}\n");
                        System.Diagnostics.Debug.WriteLine($"Failed to set wallpaper for monitor {i}: {ex.Message}"); 
                    }
                }

                if (!anySuccess) throw new Exception("All COM attempts failed.");
                desktopWallpaper.SetPosition(NativeMethods.DesktopWallpaperPosition.Fill);
                File.AppendAllText(logPath, "SetPosition(Fill) called.\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"Multi-monitor logic failed: {ex.Message}. Falling back.\n");
                System.Diagnostics.Debug.WriteLine($"Multi-monitor logic failed: {ex.Message}. Falling back.");
                await FallbackSetWallpaperAsync(state, apiResults);
            }
        });
    }

    private static async Task FallbackSetWallpaperAsync(WallpaperState state, Dictionary<TextElementState, string> apiResults)
    {
        await Task.Yield();
        string logPath = @"D:\wallpaper_debug.txt";
        File.AppendAllText(logPath, $"[{DateTime.Now}] Starting FallbackSetWallpaperAsync (Span Mode)\n");

        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
             File.AppendAllText(logPath, "No monitors found in fallback. Aborting.\n");
             return;
        }

        // 1. Calculate virtual canvas bounds (Physical pixels)
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        foreach (var m in monitors)
        {
            minX = Math.Min(minX, m.rcMonitor.Left);
            minY = Math.Min(minY, m.rcMonitor.Top);
            maxX = Math.Max(maxX, m.rcMonitor.Right);
            maxY = Math.Max(maxY, m.rcMonitor.Bottom);
        }

        int totalWidth = maxX - minX;
        int totalHeight = maxY - minY;

        File.AppendAllText(logPath, $"Fallback Canvas Size: {totalWidth}x{totalHeight} (Min: {minX},{minY})\n");

        if (totalWidth <= 0 || totalHeight <= 0) return;

        var drawingVisual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.HighQuality);

        using (var dc = drawingVisual.RenderOpen())
        {
            // Draw Black Background first
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, totalWidth, totalHeight));

            // Iterate monitors to draw content for EACH
            foreach (var monitor in monitors)
            {
                int monitorX = monitor.rcMonitor.Left - minX;
                int monitorY = monitor.rcMonitor.Top - minY;
                int monitorW = Math.Abs(monitor.rcMonitor.Right - monitor.rcMonitor.Left);
                int monitorH = Math.Abs(monitor.rcMonitor.Bottom - monitor.rcMonitor.Top);

                File.AppendAllText(logPath, $"Drawing for Monitor at ({monitorX},{monitorY}) Size {monitorW}x{monitorH}\n");

                // Draw Background Image per monitor (if needed, or just once global?)
                // Usually users want same image on each screen if "Tile/Stretch" but in Span we control it manually.
                if (!string.IsNullOrEmpty(state.BackgroundImagePath) && File.Exists(state.BackgroundImagePath))
                {
                    try 
                    {
                        var originalImage = BitmapFrame.Create(new Uri(state.BackgroundImagePath), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                        // Draw image stretched to fill THIS monitor's area
                        dc.DrawRectangle(new ImageBrush(originalImage) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center }, null, new Rect(monitorX, monitorY, monitorW, monitorH));
                    }
                    catch (Exception ex) 
                    {
                        File.AppendAllText(logPath, $"Error loading bg image: {ex.Message}\n");
                    }
                }

                // Draw Text
                double scaleX = state.DesignWidth > 0 ? (double)monitorW / state.DesignWidth : 1.0;
                double scaleY = state.DesignHeight > 0 ? (double)monitorH / state.DesignHeight : 1.0;
                double fontScale = Math.Min(scaleX, scaleY);

                foreach (var element in state.TextElements)
                {
                    string apiResultText = apiResults.TryGetValue(element, out var res) ? res : string.Empty;
                    DrawTextElementOnDrawingContext(dc, element, apiResultText, scaleX, scaleY, monitorX, monitorY);
                }
            }
        }

        var rtb = new RenderTargetBitmap(totalWidth, totalHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(drawingVisual);
        var encoder = new PngBitmapEncoder { Frames = { BitmapFrame.Create(rtb) } };
        string wallpaperPath = Path.Combine(Path.GetTempPath(), "NewDesk", "fallback_wallpaper_span.png");
        Directory.CreateDirectory(Path.GetDirectoryName(wallpaperPath)!);
        using (var fs = new FileStream(wallpaperPath, FileMode.Create, FileAccess.Write)) encoder.Save(fs);

        using (var key = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", true))
        {
            key?.SetValue("WallpaperStyle", "22"); // Span
            key?.SetValue("TileWallpaper", "0");
        }
        
        // Use SPIF_SENDWININICHANGE to ensure refresh
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperPath, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
        File.AppendAllText(logPath, $"Fallback completed. Wallpaper set to: {wallpaperPath}\n");
    }


    #region Helpers
    
    private static List<NativeMethods.MONITORINFOEX> GetMonitors()
    {
        var monitors = new List<NativeMethods.MONITORINFOEX>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new NativeMethods.MONITORINFOEX();
            if (NativeMethods.GetMonitorInfo(hMonitor, mi)) monitors.Add(mi);
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private static void DrawTextElementOnDrawingContext(
        DrawingContext dc,
        TextElementState element,
        string apiResultText,
        double scaleX,
        double scaleY,
        double offsetX = 0,
        double offsetY = 0)
    {
        string textToRender = element.Text;
        if (element.DynamicType == "GregorianDate") textToRender = GetGregorianDateString(element.DateFormat);
        else if (element.DynamicType == "LunarDate") textToRender = GetLunarDateString();
        else if (element.DynamicType == "DayOfWeek") textToRender = GetDayOfWeekString();
        else if (element.DynamicType == "Api" && !string.IsNullOrEmpty(apiResultText)) textToRender = apiResultText;

        if (string.IsNullOrEmpty(textToRender)) return;

        double fontScale = Math.Min(scaleX, scaleY);
        double fontSize = Math.Max(1.0, element.FontSize * fontScale);

        var style = element.Italic ? FontStyles.Italic : FontStyles.Normal;
        var weight = element.Bold ? FontWeights.Bold : FontWeights.Normal;
        var typeface = new Typeface(new FontFamily(element.FontFamily), style, weight, FontStretches.Normal);

        Color textColor;
        try { textColor = (Color)ColorConverter.ConvertFromString(element.Color); }
        catch { textColor = Colors.White; }
        var mainBrush = new SolidColorBrush(textColor);

        var formattedText = new FormattedText(
            textToRender,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            mainBrush,
            96.0);

        if (element.Underline)
        {
            formattedText.SetTextDecorations(TextDecorations.Underline);
        }

        formattedText.TextAlignment = element.Alignment switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        double x = offsetX + (element.X * scaleX);
        double y = offsetY + (element.Y * scaleY);
        Point location = new Point(x, y);

        // 1. Background Box
        if (element.BackgroundEnabled)
        {
            Color bgColor;
            try { bgColor = (Color)ColorConverter.ConvertFromString(element.BackgroundColor); }
            catch { bgColor = Colors.Black; }
            bgColor.A = (byte)(Math.Clamp(element.BackgroundOpacity, 0, 1) * 255);
            var bgBrush = new SolidColorBrush(bgColor);

            double pad = element.BackgroundPadding * fontScale;
            Rect bgRect = new Rect(
                x - pad,
                y - pad,
                formattedText.Width + (pad * 2),
                formattedText.Height + (pad * 2));

            double rx = element.BackgroundCornerRadius * fontScale;
            dc.DrawRoundedRectangle(bgBrush, null, bgRect, rx, rx);
        }

        // 2. Shadow
        if (element.ShadowEnabled)
        {
            Color shadowColor;
            try { shadowColor = (Color)ColorConverter.ConvertFromString(element.ShadowColor); }
            catch { shadowColor = Colors.Black; }
            shadowColor.A = (byte)(Math.Clamp(element.ShadowOpacity, 0, 1) * 255);
            var shadowBrush = new SolidColorBrush(shadowColor);

            var shadowText = new FormattedText(
                textToRender,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                shadowBrush,
                96.0)
            {
                TextAlignment = formattedText.TextAlignment
            };
            if (element.Underline) shadowText.SetTextDecorations(TextDecorations.Underline);

            Point shadowLoc = new Point(
                x + (element.ShadowOffsetX * fontScale),
                y + (element.ShadowOffsetY * fontScale));

            dc.DrawText(shadowText, shadowLoc);
        }

        // 3. Stroke
        if (element.StrokeEnabled && element.StrokeThickness > 0)
        {
            Color strokeColor;
            try { strokeColor = (Color)ColorConverter.ConvertFromString(element.StrokeColor); }
            catch { strokeColor = Colors.Black; }
            var strokePen = new Pen(new SolidColorBrush(strokeColor), element.StrokeThickness * fontScale);

            Geometry textGeom = formattedText.BuildGeometry(location);
            dc.DrawGeometry(mainBrush, strokePen, textGeom);
        }
        else
        {
            dc.DrawText(formattedText, location);
        }
    }

    private static string GetGregorianDateString(string? format = null)
    {
        if (string.IsNullOrEmpty(format)) format = "yyyy-MM-dd";
        try
        {
            return DateTime.Now.ToString(format, CultureInfo.CurrentCulture);
        }
        catch
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }
    }
    
    private static string GetDayOfWeekString() => DateTime.Now.ToString("dddd", new CultureInfo("zh-CN"));

    private static string GetLunarDateString()
    {
        try
        {
            var calendar = new ChineseLunisolarCalendar();
            var date = DateTime.Now;
            var year = calendar.GetYear(date);
            var month = calendar.GetMonth(date);
            var day = calendar.GetDayOfMonth(date);
            var leapMonth = calendar.GetLeapMonth(year);

            string[] lunarMonthNames = { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
            string[] lunarDayNames = { "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十", "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十", "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十" };

            string monthString;
            if (leapMonth > 0 && month >= leapMonth)
            {
                if (month == leapMonth)
                {
                    int prevMonthIndex = month - 2;
                    monthString = (prevMonthIndex >= 0 && prevMonthIndex < lunarMonthNames.Length) ? "闰" + lunarMonthNames[prevMonthIndex] : "闰月";
                }
                else
                {
                    int realMonthIndex = month - 2;
                    monthString = (realMonthIndex >= 0 && realMonthIndex < lunarMonthNames.Length) ? lunarMonthNames[realMonthIndex] : "未知月";
                }
            }
            else
            {
                monthString = (month - 1 >= 0 && month - 1 < lunarMonthNames.Length) ? lunarMonthNames[month - 1] : "未知月";
            }

            string dayString = (day - 1 >= 0 && day - 1 < lunarDayNames.Length) ? lunarDayNames[day - 1] : "未知日";
            return $"农历 {monthString}{dayString}";
        }
        catch { return "农历日期获取失败"; }
    }

    private static async Task<string> GetApiTextAsync(string url, string? regex, string? formatting, string? prefix = null, string? suffix = null)
    {
        try
        {
            string content = await HttpClient.GetStringAsync(url);
            if (string.IsNullOrEmpty(regex)) return content;

            var match = Regex.Match(content, regex);
            if (!match.Success) return "(Regex Fail)";

            string extractedValue = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;

            if (formatting == "流量单位转换 (B -> MB/GB)")
            {
                extractedValue = GetSize(extractedValue);
            }
            return (prefix ?? "") + extractedValue + (suffix ?? "");
        }
        catch { return "(API Fail)"; }
    }

    private static string GetSize(string bstr)
    {
        if (long.TryParse(bstr, out long b))
        {
            if (b >= 1024 * 1024 * 1024) return (b / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
            return (b / 1024.0 / 1024.0).ToString("F2") + " MB";
        }
        return bstr;
    }

    #endregion
}
