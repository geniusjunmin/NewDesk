using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NewDesk.Views;

public class PaletteCommandItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ShortcutKey { get; set; } = string.Empty;
    public Action Action { get; set; } = () => { };
}

public partial class CommandPaletteWindow : Window
{
    private List<PaletteCommandItem> _allCommands = new();

    public CommandPaletteWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        Owner = mainWindow;

        InitCommands(mainWindow);
        Loaded += (s, e) =>
        {
            FilterCommands("");
            SearchTextBox.Focus();
        };
    }

    private void InitCommands(MainWindow mainWindow)
    {
        _allCommands = new List<PaletteCommandItem>
        {
            new PaletteCommandItem { Title = "🏠 导航至 首页概览", Description = "切换至 Dashboard 仪表盘视图", ShortcutKey = "Alt+1", Action = () => mainWindow.NavigateTo("Home") },
            new PaletteCommandItem { Title = "🔑 导航至 密码管理", Description = "查看与搜索本地密码库", ShortcutKey = "Alt+2", Action = () => mainWindow.NavigateTo("Passwords") },
            new PaletteCommandItem { Title = "🔔 导航至 提醒事项", Description = "查看农历/公历提醒列表", ShortcutKey = "Alt+3", Action = () => mainWindow.NavigateTo("Reminders") },
            new PaletteCommandItem { Title = "🖼 导航至 桌面壁纸", Description = "打开壁纸库与三栏式壁纸编辑器", ShortcutKey = "Alt+4", Action = () => mainWindow.NavigateTo("Wallpaper") },
            new PaletteCommandItem { Title = "🤖 导航至 AI 助手", Description = "与本地/云端 LLM 大语言模型对话", ShortcutKey = "Alt+5", Action = () => mainWindow.NavigateTo("AiAssistant") },
            new PaletteCommandItem { Title = "⚙ 导航至 系统设置", Description = "管理配置文件、快捷键与主题外观", ShortcutKey = "Alt+6", Action = () => mainWindow.NavigateTo("Settings") },
            new PaletteCommandItem { Title = "🔒 锁定密码库", Description = "立即清空内存中的主密码并恢复锁定状态", ShortcutKey = "Ctrl+L", Action = () => Services.DataService.MasterPassword = null },
            new PaletteCommandItem { Title = "🖼 轮播下一张桌面壁纸", Description = "触发 1 次桌面壁纸轮播", ShortcutKey = "Ctrl+W", Action = () => _ = Services.WallpaperService.RotateNextAsync() }
        };
    }

    private void FilterCommands(string query)
    {
        string q = query.Trim().ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(q)
            ? _allCommands
            : _allCommands.Where(c => c.Title.ToLowerInvariant().Contains(q) || c.Description.ToLowerInvariant().Contains(q)).ToList();

        CommandsListBox.ItemsSource = filtered;
        if (CommandsListBox.Items.Count > 0)
        {
            CommandsListBox.SelectedIndex = 0;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterCommands(SearchTextBox.Text);
    }

    private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Down)
        {
            if (CommandsListBox.SelectedIndex < CommandsListBox.Items.Count - 1)
            {
                CommandsListBox.SelectedIndex++;
            }
        }
        else if (e.Key == Key.Up)
        {
            if (CommandsListBox.SelectedIndex > 0)
            {
                CommandsListBox.SelectedIndex--;
            }
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedCommand();
        }
    }

    private void CommandsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection preview
    }

    private void ExecuteSelectedCommand()
    {
        if (CommandsListBox.SelectedItem is PaletteCommandItem item)
        {
            item.Action.Invoke();
            Close();
        }
    }
}
