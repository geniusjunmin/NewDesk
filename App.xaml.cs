using System.Windows;
using NewDesk.Services;

namespace NewDesk;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Ensure icon exists
        IconService.EnsureIconExists();
        
        // Listen for new windows to set their icon
        EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            try
            {
                window.Icon = IconService.GetIconSource();
            }
            catch
            {
                // Ignore errors setting icon
            }
        }
    }
}

