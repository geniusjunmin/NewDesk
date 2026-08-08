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

    public static OperationResult SaveWallpapers(List<WallpaperState> wallpapers)
    {
        try
        {
            string json = JsonSerializer.Serialize(wallpapers, new JsonSerializerOptions { WriteIndented = true });
            SafeFileWriter.WriteAllText(AppDataPath.WallpapersFile, json);
            return OperationResult.Success("壁纸配置保存成功。");
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperService.SaveWallpapers", ex);
            return OperationResult.Fail($"壁纸配置保存失败: {ex.Message}", ex);
        }
    }

    public static OperationResult<string> SaveWallpaperAsset(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath)) return OperationResult<string>.Fail("源图片文件不存在。");

        try
        {
            string assetsDir = Path.Combine(AppDataPath.DataFolder, "Wallpapers", "Assets");
            Directory.CreateDirectory(assetsDir);

            string ext = Path.GetExtension(sourceFilePath);
            string assetId = $"{Guid.NewGuid():N}{ext}";
            string destPath = Path.Combine(assetsDir, assetId);

            byte[] data = File.ReadAllBytes(sourceFilePath);
            SafeFileWriter.WriteAllBytes(destPath, data);
            return OperationResult<string>.Success(assetId);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpaperService.SaveWallpaperAsset", ex);
            return OperationResult<string>.Fail($"壁纸资源保存失败: {ex.Message}", ex);
        }
    }

    public static string GetAssetPath(string assetId)
    {
        if (string.IsNullOrEmpty(assetId)) return string.Empty;
        if (assetId.Contains("..") || assetId.Contains('/') || assetId.Contains('\\'))
        {
            throw new ArgumentException($"非法壁纸 Asset ID 路径: '{assetId}'。不允许包含路径穿越字符。");
        }
        if (Path.IsPathRooted(assetId)) return assetId;
        return Path.Combine(AppDataPath.DataFolder, "Wallpapers", "Assets", assetId);
    }

    public static string ResolveAssetPath(string? path, string? assetId)
    {
        if (!string.IsNullOrEmpty(assetId))
        {
            try
            {
                string assetPath = GetAssetPath(assetId);
                if (File.Exists(assetPath)) return assetPath;
            }
            catch
            {
                // Safety catch for invalid asset IDs
            }
        }

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            return path;
        }

        return string.Empty;
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

    public static async Task<OperationResult> GenerateAndSetWallpaperAsync(WallpaperState state)
    {
        AppDataPath.LogInfo($"Starting GenerateAndSetWallpaperAsync for {state.Name}");

        var sources = DynamicDataService.LoadSources();
        var renderContext = await BuildRenderContextAsync(state, sources);

        string bgPath = ResolveAssetPath(state.BackgroundImagePath, state.BackgroundAssetId);

        // 2. Render and Set Desktop Wallpaper using WallpaperPreviewRenderer
        if (Application.Current?.Dispatcher == null)
        {
            return OperationResult.Fail("WPF Dispatcher 不可用，无法应用桌面壁纸。");
        }

        return await Application.Current.Dispatcher.InvokeAsync(() =>
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
                        ImageSource? bgImage = null;
                        if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
                        {
                            bgImage = BitmapFrame.Create(new Uri(bgPath), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                        }

                        WallpaperPreviewRenderer.DrawWallpaperPreview(
                            dc,
                            state.TextElements,
                            new Size(pixelWidth, pixelHeight),
                            state.DesignWidth > 0 ? state.DesignWidth : 1920,
                            state.DesignHeight > 0 ? state.DesignHeight : 1080,
                            bgImage,
                            renderContext);
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

                var applyResult = CreateApplyResult(anySuccess);
                if (!applyResult.IsSuccess) return applyResult;
                desktopWallpaper.SetPosition(NativeMethods.DesktopWallpaperPosition.Fill);
                return applyResult;
            }
            catch (Exception ex)
            {
                AppDataPath.LogError($"Multi-monitor wallpaper rendering failed: {ex.Message}", ex);
                return OperationResult.Fail($"壁纸应用失败: {ex.Message}", ex);
            }
        });
    }

    internal static OperationResult CreateApplyResult(bool anySuccess)
    {
        return anySuccess
            ? OperationResult.Success("壁纸已应用。")
            : OperationResult.Fail("壁纸应用失败: All COM attempts failed.");
    }

    internal static async Task<WallpaperRenderContext> BuildRenderContextAsync(
        WallpaperState state,
        List<DynamicDataSource> sources,
        Func<DynamicDataSource, Task<string>>? dataSourceFetcher = null,
        Func<TextElementState, Task<string>>? legacyApiFetcher = null)
    {
        dataSourceFetcher ??= source => DynamicDataService.FetchValueAsync(source);
        legacyApiFetcher ??= element => GetApiTextAsync(
            element.ApiUrl!, element.ApiRegex, element.ApiFormatting, element.ApiPrefix, element.ApiSuffix);

        var sourceMap = sources.ToDictionary(source => source.Id, source => source, StringComparer.OrdinalIgnoreCase);
        var dataSourceTasks = new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        var legacyTasks = new Dictionary<string, Task<string>>(StringComparer.Ordinal);
        var legacyElementKeys = new Dictionary<Guid, string>();

        foreach (var element in state.TextElements.Where(element => element.IsVisible))
        {
            if (!string.IsNullOrEmpty(element.DataSourceId) && sourceMap.TryGetValue(element.DataSourceId, out var source))
            {
                if (!dataSourceTasks.ContainsKey(element.DataSourceId))
                {
                    dataSourceTasks[element.DataSourceId] = dataSourceFetcher(source);
                }
            }
            else if (element.DynamicType == "Api" && !string.IsNullOrEmpty(element.ApiUrl))
            {
                string key = string.Join("\u001f", element.ApiUrl, element.ApiRegex, element.ApiFormatting, element.ApiPrefix, element.ApiSuffix);
                if (!legacyTasks.ContainsKey(key))
                {
                    legacyTasks[key] = legacyApiFetcher(element);
                }
                legacyElementKeys[element.Id] = key;
            }
        }

        await Task.WhenAll(dataSourceTasks.Values.Concat(legacyTasks.Values));
        return new WallpaperRenderContext
        {
            DataSourceValues = dataSourceTasks.ToDictionary(pair => pair.Key, pair => pair.Value.Result, StringComparer.OrdinalIgnoreCase),
            LegacyApiValues = legacyElementKeys.ToDictionary(pair => pair.Key, pair => legacyTasks[pair.Value].Result)
        };
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
