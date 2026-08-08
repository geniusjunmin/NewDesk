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

        // Initialize AppData and run versioned migrations on startup
        AppDataPath.Initialize();
        var migrationResult = MigrationService.RunAllMigrationsIfNeeded();
        if (!migrationResult.Success)
        {
            string failed = string.Join(", ", migrationResult.FailedDomains);
            AppDataPath.LogError("App.OnStartup Migration Warning", new Exception($"Domains failed migration: {failed}"));
            ToastManager.Show("数据迁移警告", $"部分数据模块 ({failed}) 升级可能未完成，旧版本数据已安全备份。", ToastType.Warning);
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
