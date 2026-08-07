using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Services;
using NewDesk.Views;

namespace NewDesk;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private GlobalHotkeyService? _hotkeyService;
    private DispatcherTimer? _autoLockTimer;

    private readonly HomeView _homeView = new();
    private readonly PasswordsView _passwordsView = new();
    private readonly RemindersView _remindersView = new();
    private readonly WallpapersView _wallpapersView = new();
    private readonly DynamicInfoView _dynamicInfoView = new();
    private readonly SettingsView _settingsView = new();
    private readonly HelpView _helpView = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Hook Window Lock / Session Switch event
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

        // View event bindings
        _homeView.RequestAddPassword += (s, e) => NavigateTo("Passwords", addPassword: true);
        _homeView.RequestAddReminder += (s, e) => NavigateTo("Reminders", addReminder: true);
        _homeView.RequestNavigateToWallpaper += (s, e) => NavigateTo("Wallpaper");

        _settingsView.RequestRerunWizard += (s, e) => RunSetupWizard();

        // Key bindings (Ctrl+F, Ctrl+N, Ctrl+,)
        KeyDown += MainWindow_KeyDown;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Initialize AppDataPath & Load settings
            AppDataPath.Initialize();
            _settings = SettingsService.LoadSettings();

            // Apply Theme
            ThemeManager.ApplyTheme(_settings.Theme);

            // Check First Run / Setup Wizard
            string[] args = Environment.GetCommandLineArgs();
            bool isAutostart = args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

            if (!_settings.HasCompletedSetupWizard)
            {
                if (!DataService.AnyDataExists())
                {
                    // Brand new user -> Launch Setup Wizard
                    RunSetupWizard();
                }
                else
                {
                    // Existing user upgrading -> mark wizard complete
                    _settings.HasCompletedSetupWizard = true;
                    SettingsService.SaveSettings(_settings);
                }
            }

            // Navigation visibility based on feature toggles
            UpdateFeatureVisibility();

            // Navigate to Home View by default
            NavigateTo("Home");

            // Load Reminders & Wallpaper services
            var reminders = DataService.LoadReminders();
            ReminderService.Start(reminders, _settings.ReminderFrequency);

            WallpaperService.InitializeDisplaySettingsListener();
            StartWallpaperService();

            // Setup Hotkey
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(this);
                    if (helper.Handle == IntPtr.Zero)
                    {
                        helper.EnsureHandle();
                    }
                    SetupHotkey();
                }
                catch (Exception ex)
                {
                    AppDataPath.LogError("MainWindow.SetupHotkey", ex);
                }
            }), DispatcherPriority.Loaded);

            // Startup Window state logic
            if (isAutostart && _settings.StartupBehavior == StartupBehavior.Tray)
            {
                Hide();
            }
            else
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            // Start Inactivity Auto-Lock Timer if configured
            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("MainWindow_Loaded", ex);
            ToastManager.Show("启动提示", $"初始化发生问题: {ex.Message}", ToastType.Warning);
        }
    }

    private bool? RunSetupWizard()
    {
        var wizard = new SetupWizardWindow(_settings) { Owner = this };
        bool? result = wizard.ShowDialog();
        if (result == true)
        {
            _settings = SettingsService.LoadSettings();
            UpdateFeatureVisibility();
            NavigateTo("Home");
            ToastManager.Show("欢迎使用", " NewDesk 已成功完成配置！", ToastType.Success);
        }
        return result;
    }

    private void UpdateFeatureVisibility()
    {
        if (NavPasswordsRadio == null) return;
        NavPasswordsRadio.Visibility = _settings.EnablePasswords ? Visibility.Visible : Visibility.Collapsed;
        NavRemindersRadio.Visibility = _settings.EnableReminders ? Visibility.Visible : Visibility.Collapsed;
        NavWallpaperRadio.Visibility = _settings.EnableWallpaper ? Visibility.Visible : Visibility.Collapsed;
        NavDynamicInfoRadio.Visibility = _settings.EnableDynamicInfo ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (MainContentControl == null) return;
        if (sender is RadioButton radio && radio.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private readonly AiAssistantView _aiAssistantView = new();

    public void NavigateTo(string target, bool addPassword = false, bool addReminder = false)
    {
        if (MainContentControl == null) return;

        UserControl targetView = target switch
        {
            "Home" => _homeView,
            "Passwords" => _passwordsView,
            "Reminders" => _remindersView,
            "Wallpaper" => _wallpapersView,
            "DynamicInfo" => _dynamicInfoView,
            "AiAssistant" => _aiAssistantView,
            "Settings" => _settingsView,
            "Help" => _helpView,
            _ => _homeView
        };

        MainContentControl.Content = targetView;

        // Sync RadioButton state safely
        if (NavHomeRadio != null)
        {
            if (target == "Home") NavHomeRadio.IsChecked = true;
            else if (target == "Passwords" && NavPasswordsRadio != null) NavPasswordsRadio.IsChecked = true;
            else if (target == "Reminders" && NavRemindersRadio != null) NavRemindersRadio.IsChecked = true;
            else if (target == "Wallpaper" && NavWallpaperRadio != null) NavWallpaperRadio.IsChecked = true;
            else if (target == "DynamicInfo" && NavDynamicInfoRadio != null) NavDynamicInfoRadio.IsChecked = true;
            else if (target == "AiAssistant" && NavAiAssistantRadio != null) NavAiAssistantRadio.IsChecked = true;
            else if (target == "Settings" && NavSettingsRadio != null) NavSettingsRadio.IsChecked = true;
            else if (target == "Help" && NavHelpRadio != null) NavHelpRadio.IsChecked = true;
        }

        UpdateSidebarVaultStatus();

        if (target == "Home")
        {
            _homeView.RefreshDashboard();
        }
        else if (target == "Passwords")
        {
            _passwordsView.CheckVaultStateAndLoad();
            if (addPassword) _passwordsView.ShowAddPasswordDialog();
        }
        else if (target == "Reminders")
        {
            _remindersView.LoadReminders();
            if (addReminder) _remindersView.ShowAddReminderDialog();
        }
        else if (target == "Wallpaper")
        {
            _wallpapersView.LoadData();
        }
    }

    public void UpdateSidebarVaultStatus()
    {
        if (SidebarLockIconText == null || SidebarLockStatusText == null) return;
        if (!string.IsNullOrEmpty(DataService.MasterPassword))
        {
            SidebarLockIconText.Text = "🔓";
            SidebarLockStatusText.Text = "密码库已解锁";
            SidebarLockStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        }
        else
        {
            SidebarLockIconText.Text = "🔒";
            SidebarLockStatusText.Text = "密码库已锁定";
            SidebarLockStatusText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Global Keyboard UX
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.F)
            {
                NavigateTo("Passwords");
                _passwordsView.FocusSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.N)
            {
                if (MainContentControl.Content == _passwordsView) _passwordsView.ShowAddPasswordDialog();
                else if (MainContentControl.Content == _remindersView) _remindersView.ShowAddReminderDialog();
                else if (MainContentControl.Content == _wallpapersView) _wallpapersView.ShowAddWallpaperDialog();
                e.Handled = true;
            }
            else if (e.Key == Key.OemComma)
            {
                NavigateTo("Settings");
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (_settings.CloseBehavior == CloseBehavior.Tray)
            {
                Hide();
            }
        }

        ResetAutoLockTimer();
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock && _settings.LockOnWindowsLock)
        {
            LockVault();
        }
    }

    private void ResetAutoLockTimer()
    {
        _autoLockTimer?.Stop();
        if (_settings.AutoLockMinutes > 0 && !string.IsNullOrEmpty(DataService.MasterPassword))
        {
            _autoLockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(_settings.AutoLockMinutes) };
            _autoLockTimer.Tick += (s, e) =>
            {
                _autoLockTimer?.Stop();
                LockVault();
                ToastManager.Show("安全提示", $"由于超过 {_settings.AutoLockMinutes} 分钟无操作，密码库已自动锁定。", ToastType.Info);
            };
            _autoLockTimer.Start();
        }
    }

    public void LockVault()
    {
        DataService.MasterPassword = null;
        UpdateSidebarVaultStatus();
        if (MainContentControl != null && MainContentControl.Content == _passwordsView)
        {
            _passwordsView.CheckVaultStateAndLoad();
        }
    }

    private void SetupHotkey()
    {
        try
        {
            _hotkeyService?.Unregister();
            _hotkeyService = new GlobalHotkeyService(this);
            _hotkeyService.Register(_settings.HotkeyModifiers, (uint)KeyInterop.VirtualKeyFromKey(_settings.Hotkey));
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("SetupHotkey", ex);
        }
    }

    private void StartWallpaperService()
    {
        try
        {
            string path = AppDataPath.WallpapersFile;
            if (!File.Exists(path) && File.Exists(Path.Combine(AppContext.BaseDirectory, "wallpapers.json")))
            {
                path = Path.Combine(AppContext.BaseDirectory, "wallpapers.json");
            }

            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var wallpaperStates = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<WallpaperState>>(json);

                if (wallpaperStates != null && wallpaperStates.Count > 0)
                {
                    if (_settings.IsWallpaperRotationEnabled)
                    {
                        WallpaperService.StartRotation(wallpaperStates, _settings.WallpaperRotationIntervalMinutes, _settings.WallpaperRotationMode);
                    }
                    else if (!string.IsNullOrEmpty(_settings.CurrentWallpaperName))
                    {
                        var state = wallpaperStates.FirstOrDefault(w => w.Name == _settings.CurrentWallpaperName);
                        if (state != null)
                        {
                            WallpaperService.StartAutoRefresh(state);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("StartWallpaperService", ex);
        }
    }

    public void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void TraySearchPassword_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
        NavigateTo("Passwords");
        _passwordsView.FocusSearch();
    }

    private void TrayAddReminder_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
        NavigateTo("Reminders", addReminder: true);
    }

    private async void TrayNextWallpaper_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await WallpaperService.RotateNextAsync();
            ToastManager.Show("壁纸", "已成功切换至下一张壁纸。", ToastType.Success);
        }
        catch
        {
            // Ignore
        }
    }

    private void TrayLockVault_Click(object sender, RoutedEventArgs e)
    {
        LockVault();
        ToastManager.Show("已锁定", "密码库已锁定。", ToastType.Info);
    }

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
        NavigateTo("Settings");
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_settings.CloseBehavior == CloseBehavior.Tray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            ReminderService.Stop();
            _hotkeyService?.Dispose();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ReminderService.Stop();
        _hotkeyService?.Dispose();
        Application.Current.Shutdown();
    }
}
