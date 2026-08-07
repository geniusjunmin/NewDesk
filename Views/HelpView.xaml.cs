using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using NewDesk.Services;

namespace NewDesk.Views;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
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

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/geniusjunmin/NewDesk",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }

    private void FeedbackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/geniusjunmin/NewDesk/issues",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }
}
