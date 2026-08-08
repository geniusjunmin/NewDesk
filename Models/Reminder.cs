using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewDesk.Models;

public enum ReminderScheduleType
{
    OneTime,
    Yearly,
    LunarYearly
}

public class Reminder : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _title = string.Empty;
    private bool _isLunar;
    private int _month;
    private int _day;
    private int _daysInAdvance = 1;
    private DateTime _lastNotifiedDate;

    // Reminder 2.0 Extended Properties
    private string _category = "工作";
    private bool _isCompleted = false;
    private DateTime? _snoozeUntil;
    private DateTime _createdTime = DateTime.Now;

    // Reminder 2.2 Time & Notification Tracking
    private DateTime? _dueAt;
    private TimeSpan? _timeOfDay;
    private string _notes = string.Empty;
    private ReminderScheduleType _scheduleType = ReminderScheduleType.OneTime;
    private DateTime? _advanceNotifiedAt;
    private DateTime? _dueNotifiedAt;

    // Occurrence Tracking for Yearly / LunarYearly Reminders
    private DateTime? _lastAdvanceOccurrence;
    private DateTime? _lastDueOccurrence;

    public Guid Id { get => _id; set => SetField(ref _id, value); }
    public string Title { get => _title; set => SetField(ref _title, value); }
    public bool IsLunar { get => _isLunar; set => SetField(ref _isLunar, value); }
    public int Month { get => _month; set => SetField(ref _month, value); }
    public int Day { get => _day; set => SetField(ref _day, value); }
    public int DaysInAdvance { get => _daysInAdvance; set => SetField(ref _daysInAdvance, value); }
    public DateTime LastNotifiedDate { get => _lastNotifiedDate; set => SetField(ref _lastNotifiedDate, value); }

    public string Category { get => _category; set => SetField(ref _category, value); }
    public bool IsCompleted { get => _isCompleted; set => SetField(ref _isCompleted, value); }
    public DateTime? SnoozeUntil { get => _snoozeUntil; set => SetField(ref _snoozeUntil, value); }
    public DateTime CreatedTime { get => _createdTime; set => SetField(ref _createdTime, value); }

    public DateTime? DueAt { get => _dueAt; set => SetField(ref _dueAt, value); }
    public TimeSpan? TimeOfDay { get => _timeOfDay; set => SetField(ref _timeOfDay, value); }
    public string Notes { get => _notes; set => SetField(ref _notes, value); }
    public ReminderScheduleType ScheduleType { get => _scheduleType; set => SetField(ref _scheduleType, value); }
    public DateTime? AdvanceNotifiedAt { get => _advanceNotifiedAt; set => SetField(ref _advanceNotifiedAt, value); }
    public DateTime? DueNotifiedAt { get => _dueNotifiedAt; set => SetField(ref _dueNotifiedAt, value); }

    public DateTime? LastAdvanceOccurrence { get => _lastAdvanceOccurrence; set => SetField(ref _lastAdvanceOccurrence, value); }
    public DateTime? LastDueOccurrence { get => _lastDueOccurrence; set => SetField(ref _lastDueOccurrence, value); }

    public string ReminderType => IsLunar ? "农历" : "公历";
    public DateTime NextReminderDate { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
