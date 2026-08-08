using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private DispatcherTimer? _autoLockTimer;
    private readonly IHotkeyRegistrationBackend _hotkeyBackend = new MultiGlobalHotkeyRegistrationBackend();

    // Phase 49: Lazy View Instantiation for view loading optimization
    private readonly Lazy<HomeView> _homeView = new(() => new HomeView());
    private readonly Lazy<PasswordsView> _passwordsView = new(() => new PasswordsView());
    private readonly Lazy<RemindersView> _remindersView = new(() => new RemindersView());
    private readonly Lazy<WallpapersView> _wallpapersView = new(() => new WallpapersView());
    private readonly Lazy<DynamicInfoView> _dynamicInfoView = new(() => new DynamicInfoView());
    private readonly Lazy<SettingsView> _settingsView = new(() => new SettingsView());
    private readonly Lazy<HelpView> _helpView = new(() => new HelpView());
    private readonly Lazy<AiAssistantView> _aiAssistantView = new(() => new AiAssistantView());

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Hook Session Lock / Switch event
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

        // Key bindings (Ctrl+F, Ctrl+N, Ctrl+,)
        KeyDown += MainWindow_KeyDown;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppDataPath.Initialize();
            _settings = SettingsService.LoadSettings();

            ThemeManager.ApplyTheme(_settings.Theme);

            string[] args = Environment.GetCommandLineArgs();
            bool isAutostart = args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

            if (!_settings.HasCompletedSetupWizard)
            {
                if (!DataService.AnyDataExists())
                {
                    RunSetupWizard();
                }
                else
                {
                    _settings.HasCompletedSetupWizard = true;
                    SettingsService.SaveSettings(_settings);
                }
            }

            // Hook view navigation events
            _homeView.Value.RequestAddPassword += (s, ev) => NavigateTo("Passwords", addPassword: true);
            _homeView.Value.RequestAddReminder += (s, ev) => NavigateTo("Reminders", addReminder: true);
            _homeView.Value.RequestNavigateToWallpaper += (s, ev) => NavigateTo("Wallpaper");

            _settingsView.Value.RequestRerunWizard += (s, ev) => RunSetupWizard();

            UpdateFeatureVisibility();
            NavigateTo("Home");

            var reminders = DataService.LoadReminders();
            ReminderService.Start(reminders, _settings.ReminderFrequency);

            WallpaperService.InitializeDisplaySettingsListener();
            StartWallpaperService();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    MultiGlobalHotkeyService.Initialize(this);
                    RegisterDynamicHotkeys();
                }
                catch (Exception ex)
                {
                    AppDataPath.LogError("MainWindow.MultiGlobalHotkeyService", ex);
                }
            }), DispatcherPriority.Loaded);

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

            ResetAutoLockTimer();
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("MainWindow_Loaded", ex);
            ToastManager.Show("启动提示", $"初始化发生问题: {ex.Message}", ToastType.Warning);
        }
    }

    // Phase 17: Multi-key combination hotkey re-registration without restart
    public void RegisterDynamicHotkeys()
    {
        var result = TryApplyHotkeyConfiguration(_settings);
        if (!result.Success)
        {
            AppDataPath.LogError("MainWindow.RegisterDynamicHotkeys", new InvalidOperationException(result.ErrorMessage));
        }
    }

    public HotkeyApplyResult TryApplyHotkeyConfiguration(AppSettings candidate)
    {
        var actions = new Dictionary<string, Action>
        {
            ["Main Window"] = ShowWindow,
            ["AI Quick Search"] = ShowAiQuickWindow,
            ["Command Palette"] = ShowCommandPalette,
            ["Clipboard AI"] = ShowClipboardAi
        };

        var result = HotkeyConfigurationTransaction.TryApply(
            _settings,
            candidate,
            _hotkeyBackend,
            actions,
            SettingsService.SaveSettings);
        if (result.Success)
        {
            _settings = candidate;
        }

        return result;
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

    public void NavigateTo(string target, bool addPassword = false, bool addReminder = false)
    {
        if (MainContentControl == null) return;

        UserControl targetView = target switch
        {
            "Home" => _homeView.Value,
            "Passwords" => _passwordsView.Value,
            "Reminders" => _remindersView.Value,
            "Wallpaper" => _wallpapersView.Value,
            "DynamicInfo" => _dynamicInfoView.Value,
            "AiAssistant" => _aiAssistantView.Value,
            "Settings" => _settingsView.Value,
            "Help" => _helpView.Value,
            _ => _homeView.Value
        };

        MainContentControl.Content = targetView;

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
            _homeView.Value.RefreshDashboard();
        }
        else if (target == "Passwords")
        {
            _passwordsView.Value.CheckVaultStateAndLoad();
            if (addPassword) _passwordsView.Value.ShowAddPasswordDialog();
        }
        else if (target == "Reminders")
        {
            _remindersView.Value.LoadReminders();
            if (addReminder) _remindersView.Value.ShowAddReminderDialog();
        }
        else if (target == "Wallpaper")
        {
            _wallpapersView.Value.LoadData();
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
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.F)
            {
                NavigateTo("Passwords");
                _passwordsView.Value.FocusSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.N)
            {
                if (MainContentControl.Content == _passwordsView.Value) _passwordsView.Value.ShowAddPasswordDialog();
                else if (MainContentControl.Content == _remindersView.Value) _remindersView.Value.ShowAddReminderDialog();
                else if (MainContentControl.Content == _wallpapersView.Value) _wallpapersView.Value.ShowAddWallpaperDialog();
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
        if (MainContentControl != null && _passwordsView.IsValueCreated && MainContentControl.Content == _passwordsView.Value)
        {
            _passwordsView.Value.CheckVaultStateAndLoad();
        }
    }

    private void StartWallpaperService()
    {
        try
        {
            string path = AppDataPath.WallpapersFile;
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

    public void ShowAiQuickWindow()
    {
        var win = new AiQuickWindow();
        win.PositionTopCenter();
        win.Show();
        win.Activate();
    }

    public void ShowCommandPalette()
    {
        var win = new CommandPaletteWindow(this);
        win.ShowDialog();
    }

    public void ShowClipboardAi()
    {
        var win = new ClipboardAiWindow();
        win.Show();
        win.Activate();
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void TraySearchPassword_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
        NavigateTo("Passwords");
        _passwordsView.Value.FocusSearch();
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
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ReminderService.Stop();
        Application.Current.Shutdown();
    }
}
