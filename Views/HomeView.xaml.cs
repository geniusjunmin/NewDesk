using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Views;

public partial class HomeView : UserControl
{
    public event EventHandler? RequestAddPassword;
    public event EventHandler? RequestAddReminder;
    public event EventHandler? RequestNavigateToWallpaper;

    public HomeView()
    {
        InitializeComponent();
        Loaded += (s, e) => RefreshDashboard();
    }

    public void RefreshDashboard()
    {
        try
        {
            var settings = SettingsService.LoadSettings();

            // Password status
            if (!string.IsNullOrEmpty(DataService.MasterPassword))
            {
                var passwords = DataService.LoadPasswords();
                PasswordVaultStatusText.Text = "已解锁 🔓";
                PasswordVaultStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                PasswordCountText.Text = $"{passwords.Count} 个密码条目";
                var latest = passwords.LastOrDefault();
                PasswordRecentText.Text = latest != null ? $"最近：{latest.Title}" : "最近：暂无条目";

                EmptyStateCard.Visibility = passwords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                PasswordVaultStatusText.Text = "已锁定 🔒";
                PasswordVaultStatusText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                PasswordCountText.Text = "密码库未解锁";
                PasswordRecentText.Text = "导航至密码页面输入主密码进行解锁";
                EmptyStateCard.Visibility = Visibility.Collapsed;
            }

            // Reminders status using ReminderScheduleCalculator
            var reminders = DataService.LoadReminders();
            int todayCount = 0;
            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            var activeOccurrences = new List<(Reminder Reminder, DateTime NextDate)>();

            foreach (var r in reminders)
            {
                if (r.IsCompleted) continue;

                var next = ReminderScheduleCalculator.GetNextOccurrence(r, now);
                if (next.HasValue)
                {
                    r.NextReminderDate = next.Value;
                    activeOccurrences.Add((r, next.Value));
                    if (next.Value.Date == today)
                    {
                        todayCount++;
                    }
                }
            }

            ReminderCountText.Text = $"共 {reminders.Count} 个提醒 （今天 {todayCount} 个）";
            var nextItem = activeOccurrences.OrderBy(x => x.NextDate).FirstOrDefault();
            ReminderNextText.Text = nextItem.Reminder != null ? $"下一个：{nextItem.NextDate:MM-dd HH:mm} {nextItem.Reminder.Title}" : "下一个提醒：暂无";

            // Wallpaper status
            WallpaperCurrentText.Text = !string.IsNullOrEmpty(settings.CurrentWallpaperName)
                ? $"当前壁纸：{settings.CurrentWallpaperName}"
                : "当前壁纸：默认";
            WallpaperRotationStatusText.Text = settings.IsWallpaperRotationEnabled
                ? $"自动轮播：开启 (每 {settings.WallpaperRotationIntervalMinutes} 分钟)"
                : "自动轮播：未开启";

            // System status
            SysHotkeyStatusText.Text = $"✓ 全局快捷键：{settings.MainWindowHotkey}";
            SysStartupStatusText.Text = settings.StartupBehavior == StartupBehavior.Tray
                ? "✓ 启动行为：后台托盘运行"
                : "✓ 启动行为：常规打开主窗口";
            SysLockStatusText.Text = string.IsNullOrEmpty(DataService.MasterPassword)
                ? "🔒 密码库状态：已锁定"
                : "🔓 密码库状态：已解锁";
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("HomeView.RefreshDashboard", ex);
        }
    }

    private void AddPasswordQuickButton_Click(object sender, RoutedEventArgs e)
    {
        RequestAddPassword?.Invoke(this, EventArgs.Empty);
    }

    private void AddReminderQuickButton_Click(object sender, RoutedEventArgs e)
    {
        RequestAddReminder?.Invoke(this, EventArgs.Empty);
    }

    private void ChangeWallpaperQuickButton_Click(object sender, RoutedEventArgs e)
    {
        RequestNavigateToWallpaper?.Invoke(this, EventArgs.Empty);
    }
}
