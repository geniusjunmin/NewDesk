using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Dialogs;

public partial class SetupWizardWindow : Window
{
    private int _currentStep = 1;
    public AppSettings Settings { get; private set; }

    public SetupWizardWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        UpdateStepUI();

        // If master password is already set from existing session or data
        if (!string.IsNullOrEmpty(DataService.MasterPassword))
        {
            WizardMasterPasswordBox.Password = DataService.MasterPassword;
            WizardConfirmPasswordBox.Password = DataService.MasterPassword;
            WizardMasterPasswordTextBox.Text = DataService.MasterPassword;
            WizardConfirmPasswordTextBox.Text = DataService.MasterPassword;
        }
    }

    private void UpdateStepUI()
    {
        Step1Grid.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Grid.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Grid.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Grid.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step5Grid.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _currentStep == 5 ? "开始使用 NewDesk" : (_currentStep == 1 ? "开始设置" : "下一步");

        StepIndicatorText.Text = $"{_currentStep} / 5";
        StepTitleText.Text = _currentStep switch
        {
            1 => "第 1 步：欢迎",
            2 => "第 2 步：安全设置",
            3 => "第 3 步：使用偏好",
            4 => "第 4 步：功能选择",
            5 => "第 5 步：完成设置",
            _ => "设置向导"
        };

        if (_currentStep == 5)
        {
            UpdateSummaryText();
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 2)
        {
            if (!ValidateStep2()) return;
        }

        if (_currentStep < 5)
        {
            _currentStep++;
            UpdateStepUI();
        }
        else
        {
            // Complete setup wizard
            SaveWizardSettings();
            DialogResult = true;
            Close();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateStepUI();
        }
    }

    private bool ValidateStep2()
    {
        Step2ErrorText.Visibility = Visibility.Collapsed;
        string password = ShowPasswordCheckBox.IsChecked == true ? WizardMasterPasswordTextBox.Text : WizardMasterPasswordBox.Password;
        string confirm = ShowPasswordCheckBox.IsChecked == true ? WizardConfirmPasswordTextBox.Text : WizardConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowStep2Error("主密码不能为空，请输入您的主密码。");
            return false;
        }

        if (password != confirm)
        {
            ShowStep2Error("两次输入的密码不一致，请重新输入。");
            return false;
        }

        // Master password verified / created
        DataService.MasterPassword = password;

        // If password file doesn't exist yet, save empty list to initialize encryption container
        if (!DataService.PasswordFileExists())
        {
            try
            {
                DataService.SavePasswords(new System.Collections.Generic.List<PasswordEntry>());
            }
            catch (Exception ex)
            {
                ShowStep2Error($"保存主密码加密容器失败: {ex.Message}");
                return false;
            }
        }

        return true;
    }

    private void ShowStep2Error(string message)
    {
        Step2ErrorText.Text = message;
        Step2ErrorText.Visibility = Visibility.Visible;
    }

    private void WizardMasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ShowPasswordCheckBox.IsChecked != true)
        {
            UpdatePasswordStrength(WizardMasterPasswordBox.Password);
        }
    }

    private void WizardMasterPasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ShowPasswordCheckBox.IsChecked == true)
        {
            UpdatePasswordStrength(WizardMasterPasswordTextBox.Text);
        }
    }

    private void WizardConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
    }

    private void WizardConfirmPasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
    }

    private void ShowPasswordCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool show = ShowPasswordCheckBox.IsChecked == true;
        if (show)
        {
            WizardMasterPasswordTextBox.Text = WizardMasterPasswordBox.Password;
            WizardConfirmPasswordTextBox.Text = WizardConfirmPasswordBox.Password;
            WizardMasterPasswordTextBox.Visibility = Visibility.Visible;
            WizardConfirmPasswordTextBox.Visibility = Visibility.Visible;
            WizardMasterPasswordBox.Visibility = Visibility.Collapsed;
            WizardConfirmPasswordBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            WizardMasterPasswordBox.Password = WizardMasterPasswordTextBox.Text;
            WizardConfirmPasswordBox.Password = WizardConfirmPasswordTextBox.Text;
            WizardMasterPasswordBox.Visibility = Visibility.Visible;
            WizardConfirmPasswordBox.Visibility = Visibility.Visible;
            WizardMasterPasswordTextBox.Visibility = Visibility.Collapsed;
            WizardConfirmPasswordTextBox.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePasswordStrength(string pwd)
    {
        if (string.IsNullOrEmpty(pwd))
        {
            PasswordStrengthProgress.Value = 0;
            PasswordStrengthText.Text = "未输入";
            PasswordStrengthText.Foreground = (Brush)FindResource("TextSecondary");
            return;
        }

        int score = 0;
        if (pwd.Length >= 6) score += 25;
        if (pwd.Length >= 10) score += 25;
        if (Regex.IsMatch(pwd, @"[a-z]") && Regex.IsMatch(pwd, @"[A-Z]")) score += 25;
        if (Regex.IsMatch(pwd, @"[0-9]") && Regex.IsMatch(pwd, @"[^a-zA-Z0-9]")) score += 25;

        PasswordStrengthProgress.Value = score;
        if (score <= 25)
        {
            PasswordStrengthText.Text = "较弱";
            PasswordStrengthText.Foreground = (Brush)FindResource("DangerBrush");
        }
        else if (score <= 50)
        {
            PasswordStrengthText.Text = "一般";
            PasswordStrengthText.Foreground = (Brush)FindResource("WarningBrush");
        }
        else if (score <= 75)
        {
            PasswordStrengthText.Text = "良好";
            PasswordStrengthText.Foreground = (Brush)FindResource("PrimaryBrush");
        }
        else
        {
            PasswordStrengthText.Text = "强";
            PasswordStrengthText.Foreground = (Brush)FindResource("SuccessBrush");
        }
    }

    private void UpdateSummaryText()
    {
        SummaryMasterPasswordText.Text = !string.IsNullOrEmpty(DataService.MasterPassword)
            ? "✓ 主密码已安全设置"
            : "⚠️ 主密码未设置";

        SummaryStartupText.Text = AutoStartCheckBox.IsChecked == true
            ? "✓ 已开启 Windows 开机自动启动"
            : "○ 未开启开机自启";

        SummaryHotkeyText.Text = "✓ 全局快捷键 Ctrl + Alt + D 已启用";

        var features = new System.Collections.Generic.List<string>();
        if (EnablePasswordsCheckBox.IsChecked == true) features.Add("密码管理");
        if (EnableRemindersCheckBox.IsChecked == true) features.Add("提醒事项");
        if (EnableWallpaperCheckBox.IsChecked == true) features.Add("桌面壁纸");
        if (EnableDynamicInfoCheckBox.IsChecked == true) features.Add("动态信息");

        SummaryFeaturesText.Text = $"✓ 已启用功能：{string.Join(" / ", features)}";
    }

    private void SaveWizardSettings()
    {
        Settings.HasCompletedSetupWizard = true;
        Settings.StartupBehavior = StartMinimizedCheckBox.IsChecked == true ? StartupBehavior.Tray : StartupBehavior.Normal;
        Settings.EnablePasswords = EnablePasswordsCheckBox.IsChecked == true;
        Settings.EnableReminders = EnableRemindersCheckBox.IsChecked == true;
        Settings.EnableWallpaper = EnableWallpaperCheckBox.IsChecked == true;
        Settings.EnableDynamicInfo = EnableDynamicInfoCheckBox.IsChecked == true;

        // Auto start configuration
        SetWindowsStartup(AutoStartCheckBox.IsChecked == true);

        SettingsService.SaveSettings(Settings);
    }

    private static void SetWindowsStartup(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
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
