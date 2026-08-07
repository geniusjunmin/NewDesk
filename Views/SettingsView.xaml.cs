using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Services;
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

    private void RebindHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        ToastManager.Show("快捷键说明", "全局快捷键目前默认设为 Ctrl + Alt + D，可以快捷显示或隐藏 NewDesk 主窗口。", ToastType.Info);
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

    private void ExportBackupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON 文件|*.json",
                FileName = $"NewDesk_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                var settingsJson = File.Exists(AppDataPath.SettingsFile) ? File.ReadAllText(AppDataPath.SettingsFile) : "{}";
                var passwordsJson = File.Exists(AppDataPath.PasswordsFile) ? File.ReadAllText(AppDataPath.PasswordsFile) : "{}";
                var remindersJson = File.Exists(AppDataPath.RemindersFile) ? File.ReadAllText(AppDataPath.RemindersFile) : "[]";

                var backupObj = new
                {
                    ExportTime = DateTime.Now,
                    Settings = settingsJson,
                    Passwords = passwordsJson,
                    Reminders = remindersJson
                };

                string backupJsonStr = System.Text.Json.JsonSerializer.Serialize(backupObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dialog.FileName, backupJsonStr);

                ToastManager.Show("成功", "所有数据已成功导出为备份文件！", ToastType.Success);
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
            var dialog = new OpenFileDialog
            {
                Filter = "JSON 文件|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                string text = File.ReadAllText(dialog.FileName);
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                var root = doc.RootElement;

                if (root.TryGetProperty("Settings", out var settingsProp))
                {
                    File.WriteAllText(AppDataPath.SettingsFile, settingsProp.GetString() ?? "");
                }
                if (root.TryGetProperty("Passwords", out var passwordsProp))
                {
                    File.WriteAllText(AppDataPath.PasswordsFile, passwordsProp.GetString() ?? "");
                }
                if (root.TryGetProperty("Reminders", out var remindersProp))
                {
                    File.WriteAllText(AppDataPath.RemindersFile, remindersProp.GetString() ?? "");
                }

                LoadSettingsUI();
                ToastManager.Show("成功", "数据已成功从备份恢复！", ToastType.Success);
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
