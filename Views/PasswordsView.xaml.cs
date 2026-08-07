using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Views;

public partial class PasswordsView : UserControl
{
    private List<PasswordEntry> _allPasswords = new();
    private DispatcherTimer? _clipboardTimer;

    public PasswordsView()
    {
        InitializeComponent();
        Loaded += (s, e) => CheckVaultStateAndLoad();
    }

    public void FocusSearch()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    public void CheckVaultStateAndLoad()
    {
        if (string.IsNullOrEmpty(DataService.MasterPassword))
        {
            LockedPanel.Visibility = Visibility.Visible;
            UnlockedVaultGrid.Visibility = Visibility.Collapsed;
            UnlockPasswordBox.Password = string.Empty;
            UnlockPasswordBox.Focus();
        }
        else
        {
            LockedPanel.Visibility = Visibility.Collapsed;
            UnlockedVaultGrid.Visibility = Visibility.Visible;
            LoadPasswords();
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        PerformUnlock();
    }

    private void UnlockPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformUnlock();
        }
    }

    private void PerformUnlock()
    {
        UnlockErrorText.Visibility = Visibility.Collapsed;
        string pwd = UnlockPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(pwd))
        {
            UnlockErrorText.Text = "请输入主密码。";
            UnlockErrorText.Visibility = Visibility.Visible;
            return;
        }

        DataService.MasterPassword = pwd;
        try
        {
            _allPasswords = DataService.LoadPasswords();
            LockedPanel.Visibility = Visibility.Collapsed;
            UnlockedVaultGrid.Visibility = Visibility.Visible;
            ApplySearchFilter();
            ToastManager.Show("解锁成功", "密码库已解锁。", ToastType.Success);
        }
        catch (CryptographicException)
        {
            DataService.MasterPassword = null;
            UnlockErrorText.Text = "主密码错误，无法解锁。";
            UnlockErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            DataService.MasterPassword = null;
            UnlockErrorText.Text = $"解锁失败: {ex.Message}";
            UnlockErrorText.Visibility = Visibility.Visible;
        }
    }

    private void LockVaultButton_Click(object sender, RoutedEventArgs e)
    {
        DataService.MasterPassword = null;
        CheckVaultStateAndLoad();
        ToastManager.Show("已锁定", "密码库已重新锁定。", ToastType.Info);
    }

    public void LoadPasswords()
    {
        try
        {
            _allPasswords = DataService.LoadPasswords();
            ApplySearchFilter();
        }
        catch (Exception ex)
        {
            ToastManager.Show("错误", $"加载密码数据失败: {ex.Message}", ToastType.Error);
        }
    }

    private void ApplySearchFilter()
    {
        string query = SearchTextBox.Text?.Trim() ?? string.Empty;
        List<PasswordEntry> filtered;
        if (string.IsNullOrEmpty(query))
        {
            filtered = _allPasswords;
        }
        else
        {
            filtered = _allPasswords.Where(p =>
                (p.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                (p.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                (p.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            ).ToList();
        }

        PasswordListView.ItemsSource = null;
        PasswordListView.ItemsSource = filtered;

        if (filtered.Count == 0)
        {
            EmptyStateBorder.Visibility = Visibility.Visible;
            EmptyStateTitle.Text = string.IsNullOrEmpty(query) ? "还没有保存任何密码" : "没有找到符合条件的密码";
        }
        else
        {
            EmptyStateBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySearchFilter();
    }

    public void ShowAddPasswordDialog()
    {
        if (string.IsNullOrEmpty(DataService.MasterPassword))
        {
            CheckVaultStateAndLoad();
            return;
        }

        var entry = new PasswordEntry { Id = Guid.NewGuid() };
        var editor = new PasswordEditorWindow(entry) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
        {
            _allPasswords.Add(entry);
            DataService.SavePasswords(_allPasswords);
            ApplySearchFilter();
            ToastManager.Show("成功", "已保存新密码条目。", ToastType.Success);
        }
    }

    private void AddPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAddPasswordDialog();
    }

    private void PasswordListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PasswordListView.SelectedItem is PasswordEntry entry)
        {
            EditPassword(entry);
        }
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PasswordEntry entry)
        {
            EditPassword(entry);
        }
    }

    private void EditPassword(PasswordEntry entry)
    {
        var editor = new PasswordEditorWindow(entry) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
        {
            DataService.SavePasswords(_allPasswords);
            ApplySearchFilter();
            ToastManager.Show("成功", "已修改密码条目。", ToastType.Success);
        }
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PasswordEntry entry)
        {
            var dialog = new ConfirmDialog($"删除密码条目", $"确定要删除“{entry.Title}”吗？此操作无法撤销。", isDanger: true)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                _allPasswords.Remove(entry);
                DataService.SavePasswords(_allPasswords);
                ApplySearchFilter();
                ToastManager.Show("已删除", $"已删除“{entry.Title}”。", ToastType.Info);
            }
        }
    }

    private void CopyUsernameItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PasswordEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Username))
            {
                ToastManager.Show("提示", "用户名为空。", ToastType.Warning);
                return;
            }
            Clipboard.SetText(entry.Username);
            ToastManager.Show("已复制", "账号已复制到剪贴板。", ToastType.Success);
        }
    }

    private void CopyPasswordItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PasswordEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Password))
            {
                ToastManager.Show("提示", "密码为空。", ToastType.Warning);
                return;
            }
            Clipboard.SetText(entry.Password);
            ToastManager.Show("已复制", "密码已复制到剪贴板（30秒后可自动清除）。", ToastType.Success);

            // Optional 30s auto clear
            var settings = SettingsService.LoadSettings();
            if (settings.AutoClearClipboard)
            {
                StartClipboardClearTimer(entry.Password, settings.AutoClearClipboardSeconds);
            }
        }
    }

    private void StartClipboardClearTimer(string expectedPassword, int seconds)
    {
        _clipboardTimer?.Stop();
        _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _clipboardTimer.Tick += (s, e) =>
        {
            _clipboardTimer?.Stop();
            try
            {
                if (Clipboard.GetText() == expectedPassword)
                {
                    Clipboard.Clear();
                    ToastManager.Show("安全提示", "剪贴板已自动清空。", ToastType.Info);
                }
            }
            catch
            {
                // Fallback
            }
        };
        _clipboardTimer.Start();
    }

    private void ImportLegacyButton_Click(object sender, RoutedEventArgs e)
    {
        int count = DataService.ImportLegacyPasswords();
        if (count < 0)
        {
            ToastManager.Show("提示", "未找到旧版 allpaw 数据文件。", ToastType.Warning);
        }
        else if (count == 0)
        {
            ToastManager.Show("提示", "allpaw 文件为空或数据已全部存在。", ToastType.Info);
        }
        else
        {
            LoadPasswords();
            ToastManager.Show("成功", $"成功导入 {count} 条旧版密码数据！", ToastType.Success);
        }
    }
}
