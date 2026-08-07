using System;
using System.Windows;
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
        
        try
        {
            TitleTextBox.Text = PasswordEntry.Title;
            UsernameTextBox.Text = PasswordEntry.Username;
            NotesTextBox.Text = PasswordEntry.Notes;
            PasswordBox.Password = PasswordEntry.Password;
            
            // 窗口加载完成后调整大小
            Loaded += (s, e) =>
            {
                UpdateWindowSize();
            };
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"初始化窗口时发生错误: {ex.Message}", Services.ToastType.Error);
        }
    }

    private void UpdateWindowSize()
    {
        try
        {
            // 强制布局更新
            MainGrid.UpdateLayout();
            
            // 测量所有控件的高度
            double totalHeight = 0;
            
            // 标题
            totalHeight += 20; // 标题高度
            totalHeight += 20; // 标题下边距
            
            // 标题输入
            TitlePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            totalHeight += TitlePanel.DesiredSize.Height;
            totalHeight += 16; // 下边距
            
            // 用户名输入
            UsernamePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            totalHeight += UsernamePanel.DesiredSize.Height;
            totalHeight += 16; // 下边距
            
            // 密码输入
            PasswordPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            totalHeight += PasswordPanel.DesiredSize.Height;
            totalHeight += 16; // 下边距
            
            // 密码选项
            OptionsPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            totalHeight += OptionsPanel.DesiredSize.Height;
            totalHeight += 16; // 下边距
            
            // 备注输入
            NotesPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            totalHeight += NotesPanel.DesiredSize.Height;
            totalHeight += 20; // 下边距
            
            // 按钮区域
            ButtonPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            totalHeight += ButtonPanel.DesiredSize.Height;
            totalHeight += 8; // 上边距
            
            // 添加 Grid 和 Border 的边距
            totalHeight += 48; // Grid Margin (24 * 2)
            totalHeight += 16; // Border Margin (8 * 2)
            totalHeight += 30; // 窗口标题栏和边框
            
            // 设置窗口高度，确保在合理范围内
            Height = Math.Max(500, Math.Min(900, totalHeight));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update window size: {ex.Message}");
            // 如果计算失败，使用默认高度
            if (Height < 500)
            {
                Height = 580;
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PasswordEntry.Title = TitleTextBox.Text;
            PasswordEntry.Username = UsernameTextBox.Text;
            PasswordEntry.Notes = NotesTextBox.Text;
            PasswordEntry.Password = (ShowPasswordCheckBox.IsChecked == true) ? PasswordTextBox.Text : PasswordBox.Password;

            DialogResult = true;
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"保存密码时发生错误: {ex.Message}", Services.ToastType.Error);
        }
    }

    private void ShowPasswordCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ShowPasswordCheckBox.IsChecked == true)
            {
                // 切换到显示密码模式
                PasswordTextBox.Text = PasswordBox.Password;
                PasswordTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 切换到隐藏密码模式
                PasswordBox.Password = PasswordTextBox.Text;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
            }
            
            // 延迟更新窗口大小，确保布局已完成
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateWindowSize();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in ShowPasswordCheckBox_Changed: {ex.Message}");
            Services.ToastManager.Show("错误", $"切换密码显示时发生错误: {ex.Message}", Services.ToastType.Error);
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var password = (ShowPasswordCheckBox.IsChecked == true) ? PasswordTextBox.Text : PasswordBox.Password;
            if (string.IsNullOrEmpty(password))
            {
                Services.ToastManager.Show("提示", "密码为空，无法复制。", Services.ToastType.Warning);
                return;
            }
            
            Clipboard.SetText(password);
            Services.ToastManager.Show("成功", "密码已复制到剪贴板。", Services.ToastType.Success);
        }
        catch (Exception ex)
        {
            Services.ToastManager.Show("错误", $"复制密码时发生错误: {ex.Message}", Services.ToastType.Error);
        }
    }
}
