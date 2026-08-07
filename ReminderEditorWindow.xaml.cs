using System;
using System.Windows;
using NewDesk.Models;
using NewDesk.Services;

namespace NewDesk;

public partial class ReminderEditorWindow : Window
{
    public Reminder Reminder { get; private set; }
    private bool _isInitializing = true;

    public ReminderEditorWindow(Reminder reminder)
    {
        InitializeComponent();
        Reminder = reminder;

        TitleTextBox.Text = Reminder.Title;
        DaysInAdvanceTextBox.Text = Reminder.DaysInAdvance.ToString();
        
        // Initialize Month ComboBox
        for (int i = 1; i <= 12; i++)
        {
            MonthComboBox.Items.Add(i);
        }

        _isInitializing = false;
        
        // Set initial values
        TypeComboBox.SelectedIndex = Reminder.IsLunar ? 1 : 0;
        MonthComboBox.SelectedItem = Reminder.Month;
        
        UpdateDayComboBox();
        DayComboBox.SelectedItem = Reminder.Day;
    }

    private void UpdateDayComboBox()
    {
        if (_isInitializing || MonthComboBox.SelectedItem == null) return;

        int month = (int)MonthComboBox.SelectedItem;
        bool isLunar = TypeComboBox.SelectedIndex == 1;
        int currentDay = DayComboBox.SelectedItem is int d ? d : 1;

        int maxDays;
        if (isLunar)
        {
            // Lunar months are 29 or 30 days. To be safe/simple for selection, allow 30.
            maxDays = 30;
        }
        else
        {
            // Gregorian days in month
            try
            {
                maxDays = DateTime.DaysInMonth(DateTime.Now.Year, month);
            }
            catch
            {
                maxDays = 31;
            }
        }

        DayComboBox.Items.Clear();
        for (int i = 1; i <= maxDays; i++)
        {
            DayComboBox.Items.Add(i);
        }

        // Restore selection if valid, otherwise select latest possible
        if (currentDay <= maxDays)
            DayComboBox.SelectedItem = currentDay;
        else
            DayComboBox.SelectedIndex = DayComboBox.Items.Count - 1;
    }

    private void TypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateDayComboBox();
    }

    private void MonthComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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

        if (MonthComboBox.SelectedItem == null || DayComboBox.SelectedItem == null)
        {
            ToastManager.Show("提示", "请选择完整的月份和日期", ToastType.Warning) ;
            return;
        }

        if (!int.TryParse(DaysInAdvanceTextBox.Text, out int daysInAdvance) || daysInAdvance < 0)
        {
            ToastManager.Show("提示", "请输入有效的提前天数", ToastType.Warning);
            return;
        }

        Reminder.Title = TitleTextBox.Text;
        Reminder.IsLunar = TypeComboBox.SelectedIndex == 1;
        Reminder.Month = (int)MonthComboBox.SelectedItem;
        Reminder.Day = (int)DayComboBox.SelectedItem;
        Reminder.DaysInAdvance = daysInAdvance;

        DialogResult = true;
    }
}
