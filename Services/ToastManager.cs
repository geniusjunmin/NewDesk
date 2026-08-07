using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace NewDesk.Services;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public static class ToastManager
{
    private static readonly ConcurrentQueue<ReminderToastWindow> _toastQueue = new();

    public static void Show(string title, string message, ToastType type = ToastType.Info, bool autoClose = true)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var toast = new ReminderToastWindow(title, message, type, autoClose);
            if (File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico")))
            {
               // Optional: Set icon if window supports it
            }
            
            // Simple stacking logic:
            // For now, we just show them. If we want true stacking, we need to track open windows.
            // Let's implement a simple offset based on open windows.
            
            int openToastCount = Application.Current.Windows.OfType<ReminderToastWindow>().Count();
            toast.Tag = openToastCount; // Store index to calculate offset if needed in the window itself
            
            toast.Show();
        });
    }
}
