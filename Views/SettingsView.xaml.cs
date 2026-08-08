using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Models.Ai;
using NewDesk.Services;
using NewDesk.Services.Security;
using ThemeMode = NewDesk.Models.ThemeMode;

namespace NewDesk.Views;

public partial class SettingsView : UserControl
{
    private AppSettings _settings = new();
    private bool _isInitializing = true;
    public event EventHandler? RequestRerunWizard;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (s, e) => LoadSettingsUI();
    }

    private void LoadSettingsUI()
    {
        _isInitializing = true;
        try
        {
            _settings = SettingsService.LoadSettings();

            DataPathText.Text = $"数据文件所在路径：{AppDataPath.DataFolder}";

            AutoStartCheckBox.IsChecked = IsStartupEnabled();

            StartupBehaviorNormalRadio.IsChecked = _settings.StartupBehavior == StartupBehavior.Normal;
            StartupBehaviorTrayRadio.IsChecked = _settings.StartupBehavior == StartupBehavior.Tray;

            CloseBehaviorTrayRadio.IsChecked = _settings.CloseBehavior == CloseBehavior.Tray;
            CloseBehaviorExitRadio.IsChecked = _settings.CloseBehavior == CloseBehavior.Exit;

            EnableNotificationsCheckBox.IsChecked = _settings.EnableNotifications;

            ReminderFrequencyComboBox.SelectedIndex = (int)_settings.ReminderFrequency;

            AutoLockComboBox.SelectedIndex = _settings.AutoLockMinutes switch
            {
                0 => 0,
                5 => 1,
                15 => 2,
                30 => 3,
                60 => 4,
                _ => 2
            };

            LockOnWindowsLockCheckBox.IsChecked = _settings.LockOnWindowsLock;

            EnablePasswordsCheckBox.IsChecked = _settings.EnablePasswords;
            EnableRemindersCheckBox.IsChecked = _settings.EnableReminders;
            EnableWallpaperCheckBox.IsChecked = _settings.EnableWallpaper;
            EnableDynamicInfoCheckBox.IsChecked = _settings.EnableDynamicInfo;

            ThemeSystemRadio.IsChecked = _settings.Theme == ThemeMode.System;
            ThemeLightRadio.IsChecked = _settings.Theme == ThemeMode.Light;
            ThemeDarkRadio.IsChecked = _settings.Theme == ThemeMode.Dark;

            // AI Network Mode & Privacy (Phase 16)
            NetworkModeLocalRadio.IsChecked = _settings.AiNetworkMode == AiNetworkMode.LocalOnly;
            NetworkModeAskRadio.IsChecked = _settings.AiNetworkMode == AiNetworkMode.AskBeforeCloud;
            NetworkModeAllowRadio.IsChecked = _settings.AiNetworkMode == AiNetworkMode.AllowCloud;

            AllowReminderContextCheckBox.IsChecked = _settings.AllowAiReminderContext;
            AllowWallpaperContextCheckBox.IsChecked = _settings.AllowAiWallpaperContext;
            AllowDynamicDataContextCheckBox.IsChecked = _settings.AllowAiDynamicDataContext;
            AllowPasswordMetadataCheckBox.IsChecked = _settings.AllowAiPasswordMetadata;
            AllowLogsCheckBox.IsChecked = _settings.AllowAiLogAnalysis;
            AllowClipboardCheckBox.IsChecked = _settings.AllowAiClipboard;
            AllowSystemInfoCheckBox.IsChecked = _settings.AllowAiSystemInfo;
            AllowBackgroundCloudCheckBox.IsChecked = _settings.AllowBackgroundCloudRequests;

            // Hotkey Bindings Display (Phase 17)
            TxtMainWindowHotkey.Text = _settings.MainWindowHotkey?.ToString() ?? "Ctrl + Alt + D";
            TxtAiQuickHotkey.Text = _settings.AiQuickHotkey?.ToString() ?? "Ctrl + Shift + Space";
            TxtCommandPaletteHotkey.Text = _settings.CommandPaletteHotkey?.ToString() ?? "Ctrl + K";
            TxtClipboardAiHotkey.Text = _settings.ClipboardAiHotkey?.ToString() ?? "Ctrl + Shift + A";

            LoadAiProvidersList();
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("SettingsView.LoadSettingsUI", ex);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void LoadAiProvidersList()
    {
        try
        {
            var providers = Services.Ai.AiProviderRegistry.GetAllProviders();
            AiProvidersListView.ItemsSource = providers;
        }
        catch { }
    }

    private void AddAiProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AiProviderEditorWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            LoadAiProvidersList();
            ToastManager.Show("添加成功", $"已成功添加 AI 服务 \"{dialog.Config.Name}\"！", ToastType.Success);
        }
    }

    private void EditAiProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AiProviderConfig config)
        {
            var dialog = new AiProviderEditorWindow(config) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                LoadAiProvidersList();
                ToastManager.Show("保存成功", $"AI 服务 \"{dialog.Config.Name}\" 设置已更新。", ToastType.Success);
            }
        }
    }

    private void DeleteAiProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AiProviderConfig config)
        {
            var confirm = new ConfirmDialog("删除 AI 服务", $"确定要删除 AI 服务 \"{config.Name}\" 吗？", isDanger: true)
            {
                Owner = Window.GetWindow(this)
            };
            if (confirm.ShowDialog() == true)
            {
                Services.Ai.AiProviderRegistry.DeleteProvider(config.ProviderId);
                LoadAiProvidersList();
                ToastManager.Show("已删除", $"AI 服务 \"{config.Name}\" 已被删除。", ToastType.Info);
            }
        }
    }

    private async void ScanLocalModelsButton_Click(object sender, RoutedEventArgs e)
    {
        ScanLocalModelsButton.IsEnabled = false;
        ScanLocalModelsButton.Content = "⟳ 正在扫描...";

        try
        {
            var detected = await Services.Ai.LocalModelScannerService.ScanLocalModelsAsync();
            if (detected.Count > 0)
            {
                foreach (var p in detected)
                {
                    Services.Ai.AiProviderRegistry.SaveProvider(p);
                }
                LoadAiProvidersList();
                ToastManager.Show("扫描成功", $"检测到 {detected.Count} 个本地 LLM 服务 (Ollama / LM Studio) 并已自动添加！", ToastType.Success);
            }
            else
            {
                ToastManager.Show("未检测到本地模型", "未在本地端口 (11434/1234) 检测到运行中的 Ollama 或 LM Studio。", ToastType.Info);
            }
        }
        catch (Exception ex)
        {
            ToastManager.Show("扫描异常", ex.Message, ToastType.Error);
        }
        finally
        {
            ScanLocalModelsButton.IsEnabled = true;
            ScanLocalModelsButton.Content = "🔍 扫描本地模型 (Ollama / LM Studio)";
        }
    }

    private void ImmediateSave_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        SetWindowsStartup(AutoStartCheckBox.IsChecked == true);

        _settings.StartupBehavior = StartupBehaviorNormalRadio.IsChecked == true ? StartupBehavior.Normal : StartupBehavior.Tray;
        _settings.CloseBehavior = CloseBehaviorTrayRadio.IsChecked == true ? CloseBehavior.Tray : CloseBehavior.Exit;

        _settings.EnableNotifications = EnableNotificationsCheckBox.IsChecked == true;

        _settings.AutoLockMinutes = AutoLockComboBox.SelectedIndex switch
        {
            0 => 0,
            1 => 5,
            2 => 15,
            3 => 30,
            4 => 60,
            _ => 15
        };

        _settings.LockOnWindowsLock = LockOnWindowsLockCheckBox.IsChecked == true;

        _settings.EnablePasswords = EnablePasswordsCheckBox.IsChecked == true;
        _settings.EnableReminders = EnableRemindersCheckBox.IsChecked == true;
        _settings.EnableWallpaper = EnableWallpaperCheckBox.IsChecked == true;
        _settings.EnableDynamicInfo = EnableDynamicInfoCheckBox.IsChecked == true;

        // AI Privacy Network Mode & Toggles (Phase 16)
        if (NetworkModeLocalRadio.IsChecked == true) _settings.AiNetworkMode = AiNetworkMode.LocalOnly;
        else if (NetworkModeAskRadio.IsChecked == true) _settings.AiNetworkMode = AiNetworkMode.AskBeforeCloud;
        else if (NetworkModeAllowRadio.IsChecked == true) _settings.AiNetworkMode = AiNetworkMode.AllowCloud;

        _settings.AllowAiReminderContext = AllowReminderContextCheckBox.IsChecked == true;
        _settings.AllowAiWallpaperContext = AllowWallpaperContextCheckBox.IsChecked == true;
        _settings.AllowAiDynamicDataContext = AllowDynamicDataContextCheckBox.IsChecked == true;
        _settings.AllowAiPasswordMetadata = AllowPasswordMetadataCheckBox.IsChecked == true;
        _settings.AllowAiLogAnalysis = AllowLogsCheckBox.IsChecked == true;
        _settings.AllowAiClipboard = AllowClipboardCheckBox.IsChecked == true;
        _settings.AllowAiSystemInfo = AllowSystemInfoCheckBox.IsChecked == true;
        _settings.AllowBackgroundCloudRequests = AllowBackgroundCloudCheckBox.IsChecked == true;

        SettingsService.SaveSettings(_settings);
    }

    private void ReminderFrequency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settings.ReminderFrequency = (ReminderFrequency)ReminderFrequencyComboBox.SelectedIndex;
        SettingsService.SaveSettings(_settings);
    }

    private void Theme_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        ThemeMode mode = ThemeMode.System;
        if (ThemeLightRadio.IsChecked == true) mode = ThemeMode.Light;
        if (ThemeDarkRadio.IsChecked == true) mode = ThemeMode.Dark;

        _settings.Theme = mode;
        SettingsService.SaveSettings(_settings);
        ThemeManager.ApplyTheme(mode);
    }

    // Phase 17: Interactive Hotkey Re-binding
    private void RebindHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string targetHotkey)
        {
            var dlg = new HotkeyCaptureDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && dlg.CapturedBinding != null)
            {
                var binding = dlg.CapturedBinding;
                var candidate = CloneSettings(_settings);
                switch (targetHotkey)
                {
                    case "MainWindow":
                        candidate.MainWindowHotkey = binding;
                        break;
                    case "AiQuick":
                        candidate.AiQuickHotkey = binding;
                        break;
                    case "CommandPalette":
                        candidate.CommandPaletteHotkey = binding;
                        break;
                    case "ClipboardAi":
                        candidate.ClipboardAiHotkey = binding;
                        break;
                }

                if (Application.Current.MainWindow is not MainWindow mw)
                {
                    ToastManager.Show("快捷键保存失败", "主窗口不可用，快捷键设置未更改。", ToastType.Error);
                    return;
                }

                var result = mw.TryApplyHotkeyConfiguration(candidate);
                if (!result.Success)
                {
                    ToastManager.Show("快捷键保存失败", result.ErrorMessage, ToastType.Error);
                    return;
                }

                _settings = candidate;
                TxtMainWindowHotkey.Text = _settings.MainWindowHotkey.ToString();
                TxtAiQuickHotkey.Text = _settings.AiQuickHotkey.ToString();
                TxtCommandPaletteHotkey.Text = _settings.CommandPaletteHotkey.ToString();
                TxtClipboardAiHotkey.Text = _settings.ClipboardAiHotkey.ToString();
                ToastManager.Show("快捷键保存", "快捷键已实时重置生效！", ToastType.Success);
            }
        }
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings);
        return JsonSerializer.Deserialize<AppSettings>(json)
            ?? throw new InvalidOperationException("无法创建快捷键设置候选副本。");
    }

    private void SendTestNotificationButton_Click(object sender, RoutedEventArgs e)
    {
        ReminderService.ShowTestReminder();
    }

    private void ChangeMasterPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new ChangeMasterPasswordWindow { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            ToastManager.Show("成功", "主密码已成功修改！", ToastType.Success);
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppDataPath.Initialize();
            Process.Start(new ProcessStartInfo
            {
                FileName = AppDataPath.DataFolder,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            ToastManager.Show("错误", $"无法打开数据目录: {ex.Message}", ToastType.Error);
        }
    }

    // Phase 18: Portable Backup Integration via BackupService
    private void ExportBackupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "NewDesk 备份包 (*.ndbackup)|*.ndbackup|ZIP 压缩包 (*.zip)|*.zip",
                FileName = $"NewDesk_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.ndbackup"
            };

            if (dialog.ShowDialog() == true)
            {
                BackupService.CreateBackup(dialog.FileName);
                ToastManager.Show("便携备份", "完整配置与图片资源已打包导出！", ToastType.Success);
            }
        }
        catch (Exception ex)
        {
            ToastManager.Show("导出错误", ex.Message, ToastType.Error);
        }
    }

    private void ImportBackupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = new ConfirmDialog("导入便携备份", "导入将用备份数据替换当前配置。导入前系统将自动生成恢复快照。确定要继续吗？")
            {
                Owner = Window.GetWindow(this)
            };

            if (confirm.ShowDialog() != true) return;

            var dialog = new OpenFileDialog
            {
                Filter = "NewDesk 备份包 (*.ndbackup;*.zip)|*.ndbackup;*.zip"
            };

            if (dialog.ShowDialog() == true)
            {
                bool success = BackupService.RestoreBackup(dialog.FileName);
                if (success)
                {
                    LoadSettingsUI();
                    ToastManager.Show("导入成功", "数据与资源已恢复！请注意：AI API Key 不包含在便携备份中，如需使用请重新配置。", ToastType.Success);
                }
            }
        }
        catch (Exception ex)
        {
            ToastManager.Show("导入错误", ex.Message, ToastType.Error);
        }
    }

    private void ResetAppButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ConfirmDialog("重置 NewDesk", "该操作将恢复初始配置，确定要继续吗？", isDanger: true)
        {
            Owner = Window.GetWindow(this)
        };

        if (confirm.ShowDialog() == true)
        {
            _settings = new AppSettings();
            SettingsService.SaveSettings(_settings);
            DataService.MasterPassword = null;
            LoadSettingsUI();
            ToastManager.Show("重置完成", "应用已恢复默认设置。", ToastType.Info);
        }
    }

    private void RerunWizardButton_Click(object sender, RoutedEventArgs e)
    {
        RequestRerunWizard?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("NewDesk") != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetWindowsStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    if (enable)
                    {
                        key.SetValue("NewDesk", $"\"{exePath}\" --startup");
                    }
                    else
                    {
                        key.DeleteValue("NewDesk", false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("SetWindowsStartup", ex);
        }
    }
}
