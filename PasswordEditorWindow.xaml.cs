using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk;

public partial class PasswordEditorWindow : Window
{
    public PasswordEntry PasswordEntry { get; private set; }

    public PasswordEditorWindow(PasswordEntry passwordEntry)
    {
        InitializeComponent();
        PasswordEntry = passwordEntry;

        WindowTitleText.Text = string.IsNullOrEmpty(PasswordEntry.Title) ? "添加密码" : "编辑密码";
        Title = WindowTitleText.Text;

        TitleTextBox.Text = PasswordEntry.Title;
        UsernameTextBox.Text = PasswordEntry.Username;
        NotesTextBox.Text = PasswordEntry.Notes;
        PasswordBox.Password = PasswordEntry.Password;
        PasswordTextBox.Text = PasswordEntry.Password;

        UpdatePasswordStrength(PasswordEntry.Password);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string pwd = ShowPasswordCheckBox.IsChecked == true ? PasswordTextBox.Text : PasswordBox.Password;
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            ToastManager.Show("提示", "请输入密码名称或平台。", ToastType.Warning);
            TitleTextBox.Focus();
            return;
        }

        PasswordEntry.Title = TitleTextBox.Text.Trim();
        PasswordEntry.Username = UsernameTextBox.Text.Trim();
        PasswordEntry.Notes = NotesTextBox.Text;
        PasswordEntry.Password = pwd;

        DialogResult = true;
        Close();
    }

    private void ShowPasswordCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool show = ShowPasswordCheckBox.IsChecked == true;
        if (show)
        {
            PasswordTextBox.Text = PasswordBox.Password;
            PasswordTextBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            PasswordBox.Password = PasswordTextBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordTextBox.Visibility = Visibility.Collapsed;
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        string pwd = ShowPasswordCheckBox.IsChecked == true ? PasswordTextBox.Text : PasswordBox.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            ToastManager.Show("提示", "密码为空，无法复制。", ToastType.Warning);
            return;
        }

        Clipboard.SetText(pwd);
        ToastManager.Show("成功", "密码已复制到剪贴板。", ToastType.Success);
    }

    private void GeneratePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        string newPwd = GenerateStrongPassword(16);
        PasswordBox.Password = newPwd;
        PasswordTextBox.Text = newPwd;
        UpdatePasswordStrength(newPwd);
        ToastManager.Show("随机密码", "已为您生成 16 位强随机密码！", ToastType.Success);
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ShowPasswordCheckBox.IsChecked != true)
        {
            UpdatePasswordStrength(PasswordBox.Password);
        }
    }

    private void PasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ShowPasswordCheckBox.IsChecked == true)
        {
            UpdatePasswordStrength(PasswordTextBox.Text);
        }
    }

    private void UpdatePasswordStrength(string pwd)
    {
        if (string.IsNullOrEmpty(pwd))
        {
            StrengthProgressBar.Value = 0;
            StrengthText.Text = "未输入";
            StrengthText.Foreground = (Brush)FindResource("TextSecondary");
            return;
        }

        int score = 0;
        if (pwd.Length >= 8) score += 25;
        if (pwd.Length >= 14) score += 25;
        if (Regex.IsMatch(pwd, @"[a-z]") && Regex.IsMatch(pwd, @"[A-Z]")) score += 25;
        if (Regex.IsMatch(pwd, @"[0-9]") && Regex.IsMatch(pwd, @"[^a-zA-Z0-9]")) score += 25;

        StrengthProgressBar.Value = score;
        if (score <= 25)
        {
            StrengthText.Text = "较弱";
            StrengthText.Foreground = (Brush)FindResource("DangerBrush");
        }
        else if (score <= 50)
        {
            StrengthText.Text = "一般";
            StrengthText.Foreground = (Brush)FindResource("WarningBrush");
        }
        else if (score <= 75)
        {
            StrengthText.Text = "良好";
            StrengthText.Foreground = (Brush)FindResource("PrimaryBrush");
        }
        else
        {
            StrengthText.Text = "强";
            StrengthText.Foreground = (Brush)FindResource("SuccessBrush");
        }
    }

    private static string GenerateStrongPassword(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()_+-=";
        var sb = new StringBuilder();
        var bytes = RandomNumberGenerator.GetBytes(length);
        foreach (byte b in bytes)
        {
            sb.Append(chars[b % chars.Length]);
        }
        return sb.ToString();
    }
}
