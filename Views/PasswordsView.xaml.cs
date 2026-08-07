using System;
using System.Collections.Generic;
using System.IO;
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
        if (string.IsNullOrEmpty(pwd))
        {
            UnlockErrorText.Text = "请输入主密码。";
            UnlockErrorText.Visibility = Visibility.Visible;
            return;
        }

        DataService.MasterPassword = pwd;
        try
        {
            _allPasswords = DataService.LoadPasswords();

            // If passwords file exists but loaded empty, check if legacy allpaw exists to import
            if (_allPasswords.Count == 0 && (File.Exists(Path.Combine(AppContext.BaseDirectory, "allpaw")) || File.Exists(Path.Combine(AppDataPath.DataFolder, "allpaw"))))
            {
                int count = DataService.ImportLegacyPasswords();
                if (count > 0)
                {
                    _allPasswords = DataService.LoadPasswords();
                }
            }

            LockedPanel.Visibility = Visibility.Collapsed;
            UnlockedVaultGrid.Visibility = Visibility.Visible;
            ApplySearchFilter();
            ToastManager.Show("解锁成功", "密码库已解锁。", ToastType.Success);
        }
        catch (CryptographicException)
        {
            // If decryption of passwords.json fails (e.g. overwritten by test data), attempt legacy allpaw recovery
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "allpaw")) || File.Exists(Path.Combine(AppDataPath.DataFolder, "allpaw")))
            {
                int count = DataService.ImportLegacyPasswords();
                if (count > 0)
                {
                    try
                    {
                        _allPasswords = DataService.LoadPasswords();
                        LockedPanel.Visibility = Visibility.Collapsed;
                        UnlockedVaultGrid.Visibility = Visibility.Visible;
                        ApplySearchFilter();
                        ToastManager.Show("密码库已恢复", $"主密码“{pwd}”验证成功，已从本地 allpaw 数据库恢复 {count} 条密码！", ToastType.Success);
                        return;
                    }
                    catch
                    {
                        // Fallthrough
                    }
                }
            }

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

    public void ShowAddPasswordDialog()
    {
        AddPasswordButton_Click(this, new RoutedEventArgs());
    }

    private void LoadPasswords()
    {
        try
        {
            _allPasswords = DataService.LoadPasswords();
            ApplySearchFilter();
        }
        catch (Exception ex)
        {
            ToastManager.Show("加载错误", ex.Message, ToastType.Error);
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        string search = SearchTextBox.Text.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(search)
            ? _allPasswords
            : _allPasswords.Where(p =>
                (p.Title?.ToLower().Contains(search) ?? false) ||
                (p.Username?.ToLower().Contains(search) ?? false) ||
                (p.Notes?.ToLower().Contains(search) ?? false)
            ).ToList();

        PasswordListView.ItemsSource = null;
        PasswordListView.ItemsSource = filtered;

        EmptyStateBorder.Visibility = _allPasswords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PasswordListView.Visibility = _allPasswords.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MasterPasswordWindow(isCreateMode: false) { Owner = Window.GetWindow(this) };
        var entry = new PasswordEntry { Id = Guid.NewGuid() };
        var editDialog = new MasterPasswordWindow(isCreateMode: false);
        
        // Show input dialog for title/username/password
        var titleDialog = new InputDialog("添加新密码", "请输入平台/项目名称：") { Owner = Window.GetWindow(this) };
        if (titleDialog.ShowDialog() == true && !string.IsNullOrEmpty(titleDialog.InputText))
        {
            entry.Title = titleDialog.InputText;
            var userDialog = new InputDialog("账号", "请输入账号/用户名：") { Owner = Window.GetWindow(this) };
            if (userDialog.ShowDialog() == true) entry.Username = userDialog.InputText;

            var passDialog = new InputDialog("密码", "请输入密码：") { Owner = Window.GetWindow(this) };
            if (passDialog.ShowDialog() == true) entry.Password = passDialog.InputText;

            _allPasswords.Add(entry);
            DataService.SavePasswords(_allPasswords);
            ApplySearchFilter();
            ToastManager.Show("添加成功", $"已保存密码“{entry.Title}”。", ToastType.Success);
        }
    }

    private void PasswordListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PasswordListView.SelectedItem is PasswordEntry entry)
        {
            EditItem_Click(sender, new RoutedEventArgs { Source = btnFromItem(entry) });
        }
    }

    private Button btnFromItem(PasswordEntry entry)
    {
        return new Button { Tag = entry };
    }

    private void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PasswordEntry entry)
        {
            var titleDialog = new InputDialog("编辑密码名称", "修改平台/项目名称：", entry.Title) { Owner = Window.GetWindow(this) };
            if (titleDialog.ShowDialog() == true && !string.IsNullOrEmpty(titleDialog.InputText))
            {
                entry.Title = titleDialog.InputText;
                var userDialog = new InputDialog("编辑账号", "修改账号/用户名：", entry.Username) { Owner = Window.GetWindow(this) };
                if (userDialog.ShowDialog() == true) entry.Username = userDialog.InputText;

                var passDialog = new InputDialog("编辑密码", "修改密码：", entry.Password) { Owner = Window.GetWindow(this) };
                if (passDialog.ShowDialog() == true) entry.Password = passDialog.InputText;

                DataService.SavePasswords(_allPasswords);
                ApplySearchFilter();
                ToastManager.Show("保存成功", $"已更新“{entry.Title}”。", ToastType.Success);
            }
        }
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PasswordEntry entry)
        {
            var confirm = new ConfirmDialog("删除密码", $"确定要删除“{entry.Title}”的密码记录吗？", isDanger: true)
            {
                Owner = Window.GetWindow(this)
            };

            if (confirm.ShowDialog() == true)
            {
                _allPasswords.Remove(entry);
                DataService.SavePasswords(_allPasswords);
                ApplySearchFilter();
                ToastManager.Show("已删除", $"已删除“{entry.Title}”。", ToastType.Info);
            }
        }
    }

    private void CopyPasswordItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PasswordEntry entry)
        {
            try
            {
                Clipboard.SetText(entry.Password ?? string.Empty);
                ToastManager.Show("已复制", "密码已复制到剪贴板，将在 30 秒后自动清空。", ToastType.Success);
                StartClipboardClearTimer();
            }
            catch (Exception ex)
            {
                ToastManager.Show("复制失败", ex.Message, ToastType.Error);
            }
        }
    }

    private void CopyUsernameItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PasswordEntry entry)
        {
            try
            {
                Clipboard.SetText(entry.Username ?? string.Empty);
                ToastManager.Show("已复制", "账号已复制到剪贴板。", ToastType.Info);
            }
            catch (Exception ex)
            {
                ToastManager.Show("复制失败", ex.Message, ToastType.Error);
            }
        }
    }

    private void ImportLegacyButton_Click(object sender, RoutedEventArgs e)
    {
        int imported = DataService.ImportLegacyPasswords();
        if (imported > 0)
        {
            _allPasswords = DataService.LoadPasswords();
            ApplySearchFilter();
            ToastManager.Show("导入成功", $"成功从本地 allpaw 导入 {imported} 条记录！", ToastType.Success);
        }
        else if (imported == -1)
        {
            ToastManager.Show("未找到文件", "未检测到本地 allpaw 数据库文件。", ToastType.Warning);
        }
        else
        {
            ToastManager.Show("导入提示", "未找到可导入的数据记录。", ToastType.Info);
        }
    }

    private void StartClipboardClearTimer()
    {
        _clipboardTimer?.Stop();
        _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clipboardTimer.Tick += (s, e) =>
        {
            _clipboardTimer.Stop();
            try { Clipboard.Clear(); } catch { }
        };
        _clipboardTimer.Start();
    }
}
