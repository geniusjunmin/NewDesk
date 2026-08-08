using System;
using System.Windows;
using System.Windows.Controls;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk;

public partial class ReminderEditorWindow : Window
{
    public Reminder OriginalReminder { get; }
    public Reminder EditedReminder { get; private set; }
    private bool _isInitializing = true;

    public ReminderEditorWindow(Reminder reminder)
    {
        InitializeComponent();
        OriginalReminder = reminder;

        // Create a clone for safe editing
        EditedReminder = new Reminder
        {
            Id = reminder.Id,
            Title = reminder.Title,
            IsLunar = reminder.IsLunar,
            Month = reminder.Month,
            Day = reminder.Day,
            DaysInAdvance = reminder.DaysInAdvance,
            LastNotifiedDate = reminder.LastNotifiedDate,
            Category = reminder.Category,
            IsCompleted = reminder.IsCompleted,
            SnoozeUntil = reminder.SnoozeUntil,
            CreatedTime = reminder.CreatedTime,
            DueAt = reminder.DueAt,
            TimeOfDay = reminder.TimeOfDay,
            Notes = reminder.Notes,
            ScheduleType = reminder.ScheduleType,
            AdvanceNotifiedAt = reminder.AdvanceNotifiedAt,
            DueNotifiedAt = reminder.DueNotifiedAt,
            LastAdvanceOccurrence = reminder.LastAdvanceOccurrence,
            LastDueOccurrence = reminder.LastDueOccurrence
        };

        WindowTitleText.Text = string.IsNullOrEmpty(EditedReminder.Title) ? "新建提醒事项" : "编辑提醒事项";
        Title = WindowTitleText.Text;

        TitleTextBox.Text = EditedReminder.Title;
        DaysInAdvanceTextBox.Text = EditedReminder.DaysInAdvance.ToString();
        NotesTextBox.Text = EditedReminder.Notes;

        for (int i = 1; i <= 12; i++)
        {
            MonthComboBox.Items.Add(i);
        }

        _isInitializing = false;

        ScheduleTypeComboBox.SelectedIndex = EditedReminder.ScheduleType switch
        {
            ReminderScheduleType.Yearly => 1,
            ReminderScheduleType.LunarYearly => 2,
            _ => 0
        };

        if (EditedReminder.DueAt.HasValue)
        {
            DueDatePicker.SelectedDate = EditedReminder.DueAt.Value.Date;
            DueTimeTextBox.Text = EditedReminder.DueAt.Value.ToString("HH:mm");
        }
        else
        {
            DueDatePicker.SelectedDate = DateTime.Now.Date.AddDays(1);
            DueTimeTextBox.Text = "09:00";
        }

        MonthComboBox.SelectedItem = EditedReminder.Month > 0 ? EditedReminder.Month : 1;
        UpdateDayComboBox();
        DayComboBox.SelectedItem = EditedReminder.Day > 0 ? EditedReminder.Day : 1;

        if (EditedReminder.TimeOfDay.HasValue)
        {
            YearlyTimeTextBox.Text = $"{EditedReminder.TimeOfDay.Value.Hours:D2}:{EditedReminder.TimeOfDay.Value.Minutes:D2}";
        }

        UpdatePanelVisibilities();
    }

    private void ScheduleTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePanelVisibilities();
        UpdateDayComboBox();
    }

    private void UpdatePanelVisibilities()
    {
        if (_isInitializing || OneTimePanel == null || YearlyPanel == null) return;

        int selectedIdx = ScheduleTypeComboBox.SelectedIndex;
        if (selectedIdx == 0)
        {
            OneTimePanel.Visibility = Visibility.Visible;
            YearlyPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            OneTimePanel.Visibility = Visibility.Collapsed;
            YearlyPanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateDayComboBox()
    {
        if (_isInitializing || MonthComboBox.SelectedItem == null || DayComboBox == null) return;

        int month = (int)MonthComboBox.SelectedItem;
        bool isLunar = ScheduleTypeComboBox.SelectedIndex == 2;
        int currentDay = DayComboBox.SelectedItem is int d ? d : 1;

        int maxDays = isLunar ? 30 : DateTime.DaysInMonth(2024, month);

        DayComboBox.Items.Clear();
        for (int i = 1; i <= maxDays; i++)
        {
            DayComboBox.Items.Add(i);
        }

        if (currentDay <= maxDays)
            DayComboBox.SelectedItem = currentDay;
        else
            DayComboBox.SelectedIndex = DayComboBox.Items.Count - 1;
    }

    private void MonthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDayComboBox();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            ToastManager.Show("提示", "请输入提醒标题", ToastType.Warning);
            return;
        }

        if (!int.TryParse(DaysInAdvanceTextBox.Text, out int daysInAdvance) || daysInAdvance < 0)
        {
            ToastManager.Show("提示", "请输入有效的提前天数", ToastType.Warning);
            return;
        }

        int selectedType = ScheduleTypeComboBox.SelectedIndex;
        string title = TitleTextBox.Text.Trim();
        string notes = NotesTextBox.Text.Trim();

        if (selectedType == 0)
        {
            // OneTime
            if (!DueDatePicker.SelectedDate.HasValue)
            {
                ToastManager.Show("提示", "请选择一次性提醒的到期日期！", ToastType.Warning);
                return;
            }

            if (!ReminderTimeParser.TryParseClockTime(DueTimeTextBox.Text, out var timeSpan))
            {
                ToastManager.Show("提示", "请输入有效的时间格式 (例: 09:00)！", ToastType.Warning);
                return;
            }

            DateTime dueAt = DueDatePicker.SelectedDate.Value.Date.Add(timeSpan);
            if (dueAt < DateTime.Now)
            {
                ToastManager.Show("时间提示", "一次性提醒到期时间已经过去，请设置未来的时间！", ToastType.Warning);
                return;
            }

            EditedReminder.Title = title;
            EditedReminder.ScheduleType = ReminderScheduleType.OneTime;
            EditedReminder.IsLunar = false;
            EditedReminder.DueAt = dueAt;
            EditedReminder.TimeOfDay = timeSpan;
            EditedReminder.Month = dueAt.Month;
            EditedReminder.Day = dueAt.Day;
            EditedReminder.DaysInAdvance = daysInAdvance;
            EditedReminder.Notes = notes;
            EditedReminder.IsCompleted = false;
        }
        else
        {
            // Yearly or LunarYearly
            if (MonthComboBox.SelectedItem == null || DayComboBox.SelectedItem == null)
            {
                ToastManager.Show("提示", "请选择完整的月份和日期！", ToastType.Warning);
                return;
            }

            if (!ReminderTimeParser.TryParseClockTime(YearlyTimeTextBox.Text, out var timeSpan))
            {
                ToastManager.Show("提示", "请输入有效的时间格式 (例: 09:00)！", ToastType.Warning);
                return;
            }

            int month = (int)MonthComboBox.SelectedItem;
            int day = (int)DayComboBox.SelectedItem;
            bool isLunar = selectedType == 2;

            EditedReminder.Title = title;
            EditedReminder.ScheduleType = isLunar ? ReminderScheduleType.LunarYearly : ReminderScheduleType.Yearly;
            EditedReminder.IsLunar = isLunar;
            EditedReminder.DueAt = null;
            EditedReminder.TimeOfDay = timeSpan;
            EditedReminder.Month = month;
            EditedReminder.Day = day;
            EditedReminder.DaysInAdvance = daysInAdvance;
            EditedReminder.Notes = notes;
            EditedReminder.IsCompleted = false;
        }

        DialogResult = true;
        Close();
    }
}
