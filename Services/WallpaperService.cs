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

    public static List<WallpaperState> LoadWallpapers()
    {
        try
        {
            string path = AppDataPath.WallpapersFile;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<WallpaperState>>(json) ?? GetDefaultPresets();
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperService.LoadWallpapers", ex);
        }
        return GetDefaultPresets();
    }

    public static void SaveWallpapers(List<WallpaperState> wallpapers)
    {
        try
        {
            string json = JsonSerializer.Serialize(wallpapers, new JsonSerializerOptions { WriteIndented = true });
            SafeFileWriter.WriteAllText(AppDataPath.WallpapersFile, json);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperService.SaveWallpapers", ex);
        }
    }

    public static string SaveWallpaperAsset(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath)) return sourceFilePath;

        try
        {
            string assetsDir = Path.Combine(AppDataPath.DataFolder, "Wallpapers", "Assets");
            Directory.CreateDirectory(assetsDir);

            string ext = Path.GetExtension(sourceFilePath);
            string assetFileName = $"{Guid.NewGuid():N}{ext}";
            string destPath = Path.Combine(assetsDir, assetFileName);

            byte[] data = File.ReadAllBytes(sourceFilePath);
            SafeFileWriter.WriteAllBytes(destPath, data);
            return destPath;
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperService.SaveWallpaperAsset", ex);
            return sourceFilePath;
        }
    }

    public static void InitializeDisplaySettingsListener()
    {
        if (_isDisplayListenerInitialized) return;

        SystemEvents.DisplaySettingsChanged += async (s, e) =>
        {
            await Task.Delay(2000);
            await RefreshAsync();
        };

        _isDisplayListenerInitialized = true;
    }

    public static void StartAutoRefresh(WallpaperState state)
    {
        StopAutoRefresh();
        _currentState = state;

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
        _currentState = null;
    }

    public static void StartRotation(List<WallpaperState> wallpapers, int intervalMinutes, WallpaperRotationMode mode)
    {
        StopRotation();
        if (wallpapers == null || wallpapers.Count == 0) return;

        _rotationList = wallpapers.ToList();
        _rotationMode = mode;
        _currentRotationIndex = -1;

        _rotationTimer = new DispatcherTimer();
        _rotationTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));
        _rotationTimer.Tick += async (s, e) => await RotateNextAsync();
        _rotationTimer.Start();

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
            if (_rotationList == null || _rotationList.Count == 0) return;

            if (_rotationMode == WallpaperRotationMode.Random)
            {
                var random = new Random();
                _currentRotationIndex = random.Next(_rotationList.Count);
            }
            else
            {
                _currentRotationIndex++;
                if (_currentRotationIndex >= _rotationList.Count || _currentRotationIndex < 0)
                    _currentRotationIndex = 0;
            }

            if (_currentRotationIndex >= 0 && _currentRotationIndex < _rotationList.Count)
            {
                var nextState = _rotationList[_currentRotationIndex];
                await GenerateAndSetWallpaperAsync(nextState);
                StartAutoRefresh(nextState);
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperService.RotateNextAsync", ex);
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
            AppDataPath.LogError("WallpaperService.RefreshAsync", ex);
        }
    }

    public static async Task GenerateAndSetWallpaperAsync(WallpaperState state)
    {
        AppDataPath.LogInfo($"Starting GenerateAndSetWallpaperAsync for {state.Name}");

        // 1. Fetch Dynamic Data & Legacy API Data with single-request Task deduplication per render pass
        var dataSourceTasks = new Dictionary<string, Task<string>>();
        var sourceMap = DynamicDataService.LoadSources().ToDictionary(s => s.Id, s => s);

        foreach (var element in state.TextElements.Where(e => e.IsVisible))
        {
            if (!string.IsNullOrEmpty(element.DataSourceId) && sourceMap.TryGetValue(element.DataSourceId, out var src))
            {
                if (!dataSourceTasks.ContainsKey(element.DataSourceId))
                {
                    dataSourceTasks[element.DataSourceId] = DynamicDataService.FetchValueAsync(src);
                }
            }
            else if (element.DynamicType == "Api" && !string.IsNullOrEmpty(element.ApiUrl))
            {
                string key = element.ApiUrl!;
                if (!dataSourceTasks.ContainsKey(key))
                {
                    dataSourceTasks[key] = GetApiTextAsync(element.ApiUrl!, element.ApiRegex, element.ApiFormatting, element.ApiPrefix, element.ApiSuffix);
                }
            }
        }

        await Task.WhenAll(dataSourceTasks.Values);

        var dataResults = new Dictionary<string, string>();
        foreach (var kvp in dataSourceTasks)
        {
            dataResults[kvp.Key] = kvp.Value.Result;
        }

        // 2. Render and Set Desktop Wallpaper
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var desktopWallpaper = (NativeMethods.IDesktopWallpaper)new NativeMethods.DesktopWallpaperClass();
                desktopWallpaper.Enable(true);
                uint monitorCount = desktopWallpaper.GetMonitorDevicePathCount();

                if (monitorCount == 0) throw new Exception("No monitors detected.");

                bool anySuccess = false;
                for (uint i = 0; i < monitorCount; i++)
                {
                    string monitorId = desktopWallpaper.GetMonitorDevicePathAt(i);
                    if (string.IsNullOrEmpty(monitorId)) continue;

                    desktopWallpaper.GetMonitorRECT(monitorId, out NativeMethods.RECT rect);
                    int pixelWidth = Math.Abs(rect.Right - rect.Left);
                    int pixelHeight = Math.Abs(rect.Bottom - rect.Top);

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

                        foreach (var element in state.TextElements)
                        {
                            string dataText = "";
                            if (!string.IsNullOrEmpty(element.DataSourceId) && dataResults.TryGetValue(element.DataSourceId, out var val1))
                            {
                                dataText = val1;
                            }
                            else if (!string.IsNullOrEmpty(element.ApiUrl) && dataResults.TryGetValue(element.ApiUrl, out var val2))
                            {
                                dataText = val2;
                            }

                            WallpaperTextRenderer.DrawElement(dc, element, dataText, scaleX, scaleY);
                        }
                    }

                    var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(drawingVisual);
                    var encoder = new PngBitmapEncoder { Frames = { BitmapFrame.Create(rtb) } };
                    string cacheDir = Path.Combine(AppDataPath.CacheFolder, "Wallpapers");
                    Directory.CreateDirectory(cacheDir);
                    string wallpaperPath = Path.Combine(cacheDir, $"wallpaper_{i}.png");
                    using (var fs = new FileStream(wallpaperPath, FileMode.Create, FileAccess.Write)) encoder.Save(fs);

                    try
                    {
                        desktopWallpaper.SetWallpaper(monitorId, wallpaperPath);
                        anySuccess = true;
                    }
                    catch (Exception ex)
                    {
                        AppDataPath.LogError($"Failed to set wallpaper for monitor {i}", ex);
                    }
                }

                if (!anySuccess) throw new Exception("All COM attempts failed.");
                desktopWallpaper.SetPosition(NativeMethods.DesktopWallpaperPosition.Fill);
            }
            catch (Exception ex)
            {
                AppDataPath.LogError($"Multi-monitor wallpaper rendering failed: {ex.Message}", ex);
            }
        });
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
            return (prefix ?? "") + extractedValue + (suffix ?? "");
        }
        catch { return "(API Fail)"; }
    }

    public static List<WallpaperState> GetDefaultPresets()
    {
        return new List<WallpaperState>
        {
            new WallpaperState
            {
                Name = "默认极简风桌面",
                DesignWidth = 1920,
                DesignHeight = 1080,
                TextElements = new List<TextElementState>
                {
                    new TextElementState { Text = "{公历日期}", DynamicType = "GregorianDate", X = 100, Y = 100, FontSize = 48, Bold = true },
                    new TextElementState { Text = "{星期}", DynamicType = "DayOfWeek", X = 100, Y = 170, FontSize = 28 }
                }
            }
        };
    }
}
