using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Views;

public partial class WallpapersView : UserControl
{
    private List<WallpaperState> _wallpapers = new();
    private AppSettings _settings = new();
    private bool _isInitializing = true;

    public WallpapersView()
    {
        InitializeComponent();
        Loaded += (s, e) => LoadData();
    }

    public void LoadData()
    {
        _isInitializing = true;
        try
        {
            _settings = SettingsService.LoadSettings();

            // Rotation settings UI
            EnableRotationCheckBox.IsChecked = _settings.IsWallpaperRotationEnabled;
            SequentialRadio.IsChecked = _settings.WallpaperRotationMode == WallpaperRotationMode.Sequential;
            RandomRadio.IsChecked = _settings.WallpaperRotationMode == WallpaperRotationMode.Random;

            int index = _settings.WallpaperRotationIntervalMinutes switch
            {
                5 => 0,
                10 => 1,
                15 => 2,
                30 => 3,
                60 => 4,
                120 => 5,
                _ => 3
            };
            IntervalComboBox.SelectedIndex = index;

            // Load Wallpaper states
            string path = AppDataPath.WallpapersFile;
            if (!File.Exists(path) && File.Exists(Path.Combine(AppContext.BaseDirectory, "wallpapers.json")))
            {
                path = Path.Combine(AppContext.BaseDirectory, "wallpapers.json");
            }

            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                _wallpapers = JsonSerializer.Deserialize<List<WallpaperState>>(json) ?? new List<WallpaperState>();
            }
            else
            {
                _wallpapers = new List<WallpaperState>();
            }

            WallpaperGalleryItemsControl.ItemsSource = null;
            WallpaperGalleryItemsControl.ItemsSource = _wallpapers;
            EmptyStateBorder.Visibility = _wallpapers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpapersView.LoadData", ex);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void SaveWallpapers()
    {
        try
        {
            string json = JsonSerializer.Serialize(_wallpapers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppDataPath.WallpapersFile, json);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("WallpapersView.SaveWallpapers", ex);
        }
    }

    private void RotationSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _settings.IsWallpaperRotationEnabled = EnableRotationCheckBox.IsChecked == true;
        _settings.WallpaperRotationMode = SequentialRadio.IsChecked == true ? WallpaperRotationMode.Sequential : WallpaperRotationMode.Random;

        int interval = IntervalComboBox.SelectedIndex switch
        {
            0 => 5,
            1 => 10,
            2 => 15,
            3 => 30,
            4 => 60,
            5 => 120,
            _ => 30
        };
        _settings.WallpaperRotationIntervalMinutes = interval;

        SettingsService.SaveSettings(_settings);

        if (_settings.IsWallpaperRotationEnabled)
        {
            WallpaperService.StartRotation(_wallpapers, _settings.WallpaperRotationIntervalMinutes, _settings.WallpaperRotationMode);
            ToastManager.Show("轮播已开启", $"每 {_settings.WallpaperRotationIntervalMinutes} 分钟自动切换壁纸。", ToastType.Success);
        }
        else
        {
            WallpaperService.StopRotation();
            ToastManager.Show("轮播已暂停", "壁纸自动轮播已停用。", ToastType.Info);
        }
    }

    private async void NextWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await WallpaperService.RotateNextAsync();
            ToastManager.Show("壁纸已切换", "已成功切换至下一张壁纸。", ToastType.Success);
        }
        catch (Exception ex)
        {
            ToastManager.Show("错误", $"切换壁纸失败: {ex.Message}", ToastType.Error);
        }
    }

    public void ShowAddWallpaperDialog()
    {
        var newState = new WallpaperState
        {
            Name = $"壁纸 {_wallpapers.Count + 1}",
            DesignWidth = 1920,
            DesignHeight = 1080
        };

        var editor = new WallpaperEditorWindow(newState) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
        {
            _wallpapers.Add(newState);
            SaveWallpapers();
            LoadData();
            ToastManager.Show("成功", "新壁纸模板创建成功。", ToastType.Success);
        }
    }

    private void AddWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAddWallpaperDialog();
    }

    private async void ApplyWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WallpaperState state)
        {
            _settings.CurrentWallpaperName = state.Name;
            SettingsService.SaveSettings(_settings);

            WallpaperService.StartAutoRefresh(state);
            await WallpaperService.RefreshAsync();
            ToastManager.Show("壁纸应用成功", $"已成功将“{state.Name}”设为当前壁纸。", ToastType.Success);
        }
    }

    private void EditWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WallpaperState state)
        {
            var editor = new WallpaperEditorWindow(state) { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true)
            {
                SaveWallpapers();
                LoadData();
                ToastManager.Show("成功", "壁纸配置更新成功。", ToastType.Success);
            }
        }
    }

    private void DeleteWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WallpaperState state)
        {
            var dialog = new ConfirmDialog("删除壁纸模板", $"确定要删除壁纸“{state.Name}”吗？", isDanger: true)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                _wallpapers.Remove(state);
                SaveWallpapers();
                LoadData();
                ToastManager.Show("已删除", $"已删除壁纸“{state.Name}”。", ToastType.Info);
            }
        }
    }
}
