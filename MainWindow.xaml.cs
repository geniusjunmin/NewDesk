using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk;

public partial class MainWindow : Window
{
    private List<PasswordEntry> _passwords = new();
    private List<Reminder> _reminders = new();
    private GlobalHotkeyService? _hotkeyService;
    private AppSettings _settings = new();
    private bool _isMasterPasswordVerified = false;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        
        // Set icon after initialization
        try
        {
            var iconSource = IconService.GetIconSource();
            this.Icon = iconSource;
            MyNotifyIcon.IconSource = iconSource;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set icon: {ex.Message}");
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadSettings();
            LoadReminders();
            
            // Initialize display change listener
            WallpaperService.InitializeDisplaySettingsListener();
            
            // Setup hotkey after window is fully loaded and handle is created
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Ensure window handle is created
                    var helper = new System.Windows.Interop.WindowInteropHelper(this);
                    if (helper.Handle == IntPtr.Zero)
                    {
                        helper.EnsureHandle();
                    }
                    SetupHotkey();
                }
                catch (Exception ex)
                {
                    // Log error but don't crash
                    System.Diagnostics.Debug.WriteLine($"Failed to setup hotkey: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            
            StartReminderService();
            
            // Always hide to system tray on startup as per user request
            Hide();

            // Start Wallpaper Service (Single or Rotation)
            try
            {
                string wallpapersPath = System.IO.Path.Combine(AppContext.BaseDirectory, "wallpapers.json");
                if (System.IO.File.Exists(wallpapersPath))
                {
                    string json = System.IO.File.ReadAllText(wallpapersPath);
                    var wallpaperStates = System.Text.Json.JsonSerializer.Deserialize<List<NewDesk.Models.WallpaperState>>(json);
                    
                    if (wallpaperStates != null && wallpaperStates.Count > 0)
                    {
                        if (_settings.IsWallpaperRotationEnabled)
                        {
                            NewDesk.Services.WallpaperService.StartRotation(wallpaperStates, _settings.WallpaperRotationIntervalMinutes, _settings.WallpaperRotationMode);
                        }
                        else if (!string.IsNullOrEmpty(_settings.CurrentWallpaperName))
                        {
                            var state = wallpaperStates.FirstOrDefault(w => w.Name == _settings.CurrentWallpaperName);
                            if (state != null)
                            {
                                NewDesk.Services.WallpaperService.StartAutoRefresh(state);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start wallpaper service: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"应用启动时发生错误: {ex.Message}", Services.ToastType.Error);
            Application.Current.Shutdown();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void ShowWindow()
    {
        if (EnsureMasterPasswordVerified())
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private bool EnsureMasterPasswordVerified()
    {
        if (_isMasterPasswordVerified) return true;

        try
        {
            if (DataService.PasswordFileExists())
            {
                var masterPasswordWindow = new MasterPasswordWindow(false);
                if (masterPasswordWindow.ShowDialog() == true)
                {
                    DataService.MasterPassword = masterPasswordWindow.Password;
                    // Attempt to load passwords to verify the master password
                    LoadPasswords();
                    _isMasterPasswordVerified = true;
                    return true;
                }
            }
            else
            {
                // First time setup
                var masterPasswordWindow = new MasterPasswordWindow(true);
                if (masterPasswordWindow.ShowDialog() == true)
                {
                    DataService.MasterPassword = masterPasswordWindow.Password;
                    // For first time, we still need to initialize the file or just mark as verified
                    // LoadPasswords will return empty but valid
                    LoadPasswords();
                    _isMasterPasswordVerified = true;
                    return true;
                }
            }
        }
        catch (CryptographicException)
        {
            Services.ToastManager.Show("错误", "主密码错误，无法解锁。", Services.ToastType.Error);
            DataService.MasterPassword = null; // Reset for next attempt
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"验证主密码时发生错误: {ex.Message}", Services.ToastType.Error);
        }

        return false;
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ReminderService.Stop();
        _hotkeyService?.Dispose();
        Application.Current.Shutdown();
    }

    private void LoadSettings()
    {
        try
        {
            _settings = SettingsService.LoadSettings();
            if (StartupCheckBox != null)
            {
                StartupCheckBox.IsChecked = IsStartupEnabled();
            }
            if (ReminderFrequencyComboBox != null && ReminderFrequencyComboBox.Items.Count > 0)
            {
                var index = (int)_settings.ReminderFrequency;
                if (index >= 0 && index < ReminderFrequencyComboBox.Items.Count)
                {
                    ReminderFrequencyComboBox.SelectedIndex = index;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            _settings = new AppSettings();
        }
    }

    private void LoadPasswords()
    {
        try
        {
            _passwords = DataService.LoadPasswords();
            RefreshPasswordList();
        }
        catch (CryptographicException ex)
        {
            Services.ToastManager.Show("错误", "主密码错误，无法加载密码数据。", Services.ToastType.Error);
            System.Diagnostics.Debug.WriteLine($"CryptographicException: {ex.Message}");
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"加载密码数据时发生错误: {ex.Message}", Services.ToastType.Error);
            System.Diagnostics.Debug.WriteLine($"Exception in LoadPasswords: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void RefreshPasswordList()
    {
        try
        {
            if (PasswordListView != null)
            {
                PasswordListView.ItemsSource = null;
                PasswordListView.ItemsSource = _passwords;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to refresh password list: {ex.Message}");
        }
    }

    private void LoadReminders()
    {
        _reminders = DataService.LoadReminders();
        RefreshReminderList();
    }

    private void RefreshReminderList()
    {
        try
        {
            if (ReminderListView != null)
            {
                ReminderListView.ItemsSource = null;
                ReminderListView.ItemsSource = _reminders;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to refresh reminder list: {ex.Message}");
        }
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (SearchPlaceholder != null)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        var searchText = SearchTextBox.Text.ToLower();
        if (string.IsNullOrWhiteSpace(searchText))
        {
            RefreshPasswordList();
            return;
        }
        PasswordListView.ItemsSource = _passwords.Where(p =>
            p.Title.ToLower().Contains(searchText) ||
            p.Username.ToLower().Contains(searchText) ||
            p.Notes.ToLower().Contains(searchText)
        );
    }

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SearchTextBox.Text))
        {
            SearchPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void PasswordListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditButton_Click(sender, e);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var newEntry = new PasswordEntry { Id = Guid.NewGuid() };
        var editor = new PasswordEditorWindow(newEntry);
        if (editor.ShowDialog() == true)
        {
            _passwords.Add(editor.PasswordEntry);
            DataService.SavePasswords(_passwords);
            RefreshPasswordList();
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordListView.SelectedItem is PasswordEntry selected)
        {
            var editor = new PasswordEditorWindow(selected);
            if (editor.ShowDialog() == true)
            {
                DataService.SavePasswords(_passwords);
                RefreshPasswordList();
            }
        }
        else
        {
            Services.ToastManager.Show("提示", "请先选择一个密码条目。", Services.ToastType.Warning);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordListView.SelectedItem is PasswordEntry selected)
        {
            if (MessageBox.Show("确定要删除这个密码条目吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _passwords.Remove(selected);
                DataService.SavePasswords(_passwords);
                RefreshPasswordList();
            }
        }
        else
        {
            Services.ToastManager.Show("提示", "请先选择一个密码条目。", Services.ToastType.Warning);
        }
    }

    private void ImportLegacyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int count = DataService.ImportLegacyPasswords();
            LoadPasswords();
            Services.ToastManager.Show("成功", $"成功导入 {count} 个密码条目。", Services.ToastType.Success);
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"导入失败: {ex.Message}", Services.ToastType.Error);
        }
    }

    private void AddReminderButton_Click(object sender, RoutedEventArgs e)
    {
        var newReminder = new Reminder
        {
            Id = Guid.NewGuid(),
            Month = DateTime.Now.Month,
            Day = DateTime.Now.Day
        };
        var editor = new ReminderEditorWindow(newReminder);
        if (editor.ShowDialog() == true)
        {
            _reminders.Add(editor.Reminder);
            DataService.SaveReminders(_reminders);
            RefreshReminderList();
            ReminderService.Start(_reminders, _settings.ReminderFrequency);
        }
    }

    private void EditReminderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderListView.SelectedItem is Reminder selected)
        {
            var editor = new ReminderEditorWindow(selected);
            if (editor.ShowDialog() == true)
            {
                DataService.SaveReminders(_reminders);
                RefreshReminderList();
                ReminderService.Start(_reminders, _settings.ReminderFrequency);
            }
        }
        else
        {
            Services.ToastManager.Show("提示", "请先选择一个提醒条目。", Services.ToastType.Warning);
        }
    }

    private void DeleteReminderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderListView.SelectedItem is Reminder selected)
        {
            if (MessageBox.Show("确定要删除这个提醒吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _reminders.Remove(selected);
                DataService.SaveReminders(_reminders);
                RefreshReminderList();
                ReminderService.Start(_reminders, _settings.ReminderFrequency);
            }
        }
        else
        {
            Services.ToastManager.Show("提示", "请先选择一个提醒条目。", Services.ToastType.Warning);
        }
    }

    private void TestReminderButton_Click(object sender, RoutedEventArgs e)
    {
        ReminderService.ShowTestReminder();
    }

    private void OpenWallpaperEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new WallpaperEditorWindow();
        editor.Show();
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        SetStartup(StartupCheckBox.IsChecked == true);
    }

    private void HotkeyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var modifiers = Keyboard.Modifiers;
        uint modFlags = 0;
        if ((modifiers & ModifierKeys.Alt) != 0) modFlags |= HotkeyModifiers.Alt;
        if ((modifiers & ModifierKeys.Control) != 0) modFlags |= HotkeyModifiers.Ctrl;
        if ((modifiers & ModifierKeys.Shift) != 0) modFlags |= HotkeyModifiers.Shift;
        if ((modifiers & ModifierKeys.Windows) != 0) modFlags |= HotkeyModifiers.Win;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        HotkeyTextBox.Text = $"{modifiers} + {key}";
        _settings.HotkeyModifiers = modFlags;
        _settings.Hotkey = key;
    }

    private void SaveHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.SaveSettings(_settings);
        SetupHotkey();
        Services.ToastManager.Show("成功", "快捷键已保存。", Services.ToastType.Success);
    }

    private void ReminderFrequencyComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ReminderFrequencyComboBox.SelectedIndex >= 0)
        {
            _settings.ReminderFrequency = (ReminderFrequency)ReminderFrequencyComboBox.SelectedIndex;
            SettingsService.SaveSettings(_settings);
            ReminderService.Start(_reminders, _settings.ReminderFrequency);
        }
    }

    private void ChangeMasterPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var changeWindow = new ChangeMasterPasswordWindow();
        if (changeWindow.ShowDialog() == true)
        {
            try
            {
                // Verify old password by trying to load passwords
                var oldPassword = DataService.MasterPassword;
                DataService.MasterPassword = changeWindow.OldPassword;
                var testLoad = DataService.LoadPasswords();
                
                // If we get here, old password is correct. Now re-encrypt with new password
                DataService.MasterPassword = changeWindow.NewPassword;
                DataService.SavePasswords(_passwords);
                
                Services.ToastManager.Show("成功", "主密码已修改。", Services.ToastType.Success);
            }
            catch (CryptographicException)
            {
                Services.ToastManager.Show("错误", "旧密码错误，无法修改主密码。", Services.ToastType.Error);
            }
            catch (Exception ex)
            {
                Services.ToastManager.Show("错误", $"修改主密码失败: {ex.Message}", Services.ToastType.Error);
            }
        }
    }

    private void SetupHotkey()
    {
        try
        {
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            
            // Wait for window handle to be created
            if (!IsLoaded)
            {
                return;
            }
            
            // Force window handle creation
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                // Window handle not ready yet, try again later
                Dispatcher.BeginInvoke(new Action(() => SetupHotkey()), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            
            _hotkeyService = new GlobalHotkeyService(this);
            _hotkeyService.Register(_settings.HotkeyModifiers, (uint)KeyInterop.VirtualKeyFromKey(_settings.Hotkey));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to setup hotkey: {ex.Message}");
            // Don't throw, just log the error
        }
    }

    private void StartReminderService()
    {
        try
        {
            ReminderService.Start(_reminders, _settings.ReminderFrequency);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start reminder service: {ex.Message}");
            // Don't crash the app if reminder service fails
        }
    }

    private bool IsStartupEnabled()
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

    private void SetStartup(bool enable)
    {
        try
        {
            var fileName = Environment.ProcessPath;
            if (string.IsNullOrEmpty(fileName))
            {
                throw new InvalidOperationException("Could not determine executable path.");
            }

            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null)
            {
                throw new InvalidOperationException("Could not open registry key for startup.");
            }

            if (enable)
            {
                // Quote the path to handle spaces
                key.SetValue("NewDesk", $"\"{fileName}\"");
            }
            else
            {
                key.DeleteValue("NewDesk", false);
            }
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"设置开机自启动失败: {ex.Message}", Services.ToastType.Error);
        }
    }
}
