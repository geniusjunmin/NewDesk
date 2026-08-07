using System;
using System.Linq;
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

        // Automated Test Runner Flag
        if (e.Args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                int exitCode = AutomatedTestRunner.RunAllTestsAsync().GetAwaiter().GetResult();
                Shutdown(exitCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ TEST FAILURE EXCEPTION: {ex.Message}");
                Shutdown(1);
            }
            return;
        }

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
