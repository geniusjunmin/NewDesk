using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NewDesk;

public partial class ReminderToastWindow : Window
{
    private bool _autoClose;

    public ReminderToastWindow(string title, string message, Services.ToastType type = Services.ToastType.Info, bool autoClose = true)
    {
        InitializeComponent();
        _autoClose = autoClose;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;

        // Set colors based on type
        var brush = Application.Current.Resources["PrimaryColor"] as SolidColorBrush ?? Brushes.DodgerBlue;
        switch (type)
        {
            case Services.ToastType.Success: brush = new SolidColorBrush(Color.FromRgb(76, 175, 80)); break; // Green
            case Services.ToastType.Warning: brush = new SolidColorBrush(Color.FromRgb(255, 152, 0)); break; // Orange
            case Services.ToastType.Error: brush = new SolidColorBrush(Color.FromRgb(244, 67, 54)); break; // Red
            default: break; // Default Blue
        }
        TitleTextBlock.Foreground = brush;
        
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workingArea = SystemParameters.WorkArea;
        
        // Calculate offset based on other open toast windows
        int openCount = 0;
        foreach (Window window in Application.Current.Windows)
        {
            if (window is ReminderToastWindow && window != this && window.IsLoaded)
            {
                openCount++;
            }
        }

        // Each toast is ~120px high + 10px margin
        double offset = openCount * (Height + 10);

        Left = workingArea.Right - Width - 10;
        Top = workingArea.Bottom;

        var slideInAnimation = new DoubleAnimation
        {
            From = workingArea.Bottom,
            To = workingArea.Bottom - Height - 10 - offset,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(TopProperty, slideInAnimation);

        if (_autoClose)
        {
            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.5),
                BeginTime = TimeSpan.FromSeconds(4)
            };

            fadeOutAnimation.Completed += (s, a) => Close();
            BeginAnimation(OpacityProperty, fadeOutAnimation);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
