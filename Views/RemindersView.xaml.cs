using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk.Views;

public partial class RemindersView : UserControl
{
    private List<Reminder> _reminders = new();

    public RemindersView()
    {
        InitializeComponent();
        Loaded += (s, e) => LoadReminders();
    }

    public void LoadReminders()
    {
        try
        {
            _reminders = DataService.LoadReminders();
            DateTime today = DateTime.Today;

            foreach (var r in _reminders)
            {
                ReminderService.UpdateNextReminderDate(r);
            }

            var todayList = _reminders.Where(r => r.NextReminderDate.Date == today).OrderBy(r => r.NextReminderDate).ToList();
            var upcomingList = _reminders.Where(r => r.NextReminderDate.Date > today && r.NextReminderDate.Date <= today.AddDays(30)).OrderBy(r => r.NextReminderDate).ToList();
            var laterList = _reminders.Where(r => r.NextReminderDate.Date > today.AddDays(30)).OrderBy(r => r.NextReminderDate).ToList();

            var itemTemplate = (DataTemplate)Resources["ReminderItemTemplate"];

            TodayItemsControl.ItemTemplate = itemTemplate;
            TodayItemsControl.ItemsSource = todayList;
            TodayBadgeText.Text = todayList.Count.ToString();
            TodayGroupPanel.Visibility = todayList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            UpcomingItemsControl.ItemTemplate = itemTemplate;
            UpcomingItemsControl.ItemsSource = upcomingList;
            UpcomingBadgeText.Text = upcomingList.Count.ToString();
            UpcomingGroupPanel.Visibility = upcomingList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            LaterItemsControl.ItemTemplate = itemTemplate;
            LaterItemsControl.ItemsSource = laterList;
            LaterBadgeText.Text = laterList.Count.ToString();
            LaterGroupPanel.Visibility = laterList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            EmptyStateBorder.Visibility = _reminders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("RemindersView.LoadReminders", ex);
        }
    }

    public void ShowAddReminderDialog()
    {
        var reminder = new Reminder
        {
            Id = Guid.NewGuid(),
            Month = DateTime.Now.Month,
            Day = DateTime.Now.Day,
            DaysInAdvance = 0
        };

        var editor = new ReminderEditorWindow(reminder) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
        {
            _reminders.Add(reminder);
            SaveAndReload();
            ToastManager.Show("成功", "已创建新提醒事项。", ToastType.Success);
        }
    }

    private void AddReminderButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAddReminderDialog();
    }

    private void EditReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Reminder reminder)
        {
            var editor = new ReminderEditorWindow(reminder) { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true)
            {
                SaveAndReload();
                ToastManager.Show("成功", "已修改提醒事项。", ToastType.Success);
            }
        }
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Reminder reminder)
        {
            var dialog = new ConfirmDialog("删除提醒事项", $"确定要删除提醒“{reminder.Title}”吗？", isDanger: true)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                _reminders.Remove(reminder);
                SaveAndReload();
                ToastManager.Show("已删除", $"已删除“{reminder.Title}”。", ToastType.Info);
            }
        }
    }

    private void SaveAndReload()
    {
        DataService.SaveReminders(_reminders);
        var settings = SettingsService.LoadSettings();
        ReminderService.Start(_reminders, settings.ReminderFrequency);
        LoadReminders();
    }
}
