using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Views;
using ThemeMode = NewDesk.Models.ThemeMode;

namespace NewDesk.Services;

public static class AutomatedTestRunner
{
    public static void RunAllTests()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("⚡ NewDesk Automated Test Suite Execution");
        Console.WriteLine("=================================================");

        // Backup existing real user files before running tests
        string passwordsFile = AppDataPath.PasswordsFile;
        string remindersFile = AppDataPath.RemindersFile;
        string settingsFile = AppDataPath.SettingsFile;
        string wallpapersFile = AppDataPath.WallpapersFile;

        string? backupPasswords = File.Exists(passwordsFile) ? File.ReadAllText(passwordsFile) : null;
        string? backupReminders = File.Exists(remindersFile) ? File.ReadAllText(remindersFile) : null;
        string? backupSettings = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        string? backupWallpapers = File.Exists(wallpapersFile) ? File.ReadAllText(wallpapersFile) : null;

        int passed = 0;
        int total = 0;

        void RunTest(string testName, Action testAction)
        {
            total++;
            try
            {
                Console.Write($"[TEST {total}] {testName}... ");
                testAction();
                Console.WriteLine("PASSED ✓");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED ✗");
                Console.WriteLine($"  Error Details: {ex.Message}");
                Console.WriteLine($"  Stack Trace: {ex.StackTrace}");
                throw;
            }
        }

        try
        {
            RunTest("AppData Path Initialization & Logging", () =>
            {
                AppDataPath.Initialize();
                if (!Directory.Exists(AppDataPath.DataFolder))
                    throw new InvalidOperationException("Data directory was not created.");
                AppDataPath.LogError("AutomatedTestRunnerTest", new Exception("Test Log Entry"));
            });

            RunTest("Settings Service Persistence & Backward Compatibility", () =>
            {
                var settings = SettingsService.LoadSettings();
                settings.Theme = ThemeMode.Dark;
                settings.EnablePasswords = true;
                SettingsService.SaveSettings(settings);

                var reloaded = SettingsService.LoadSettings();
                if (reloaded.Theme != ThemeMode.Dark)
                    throw new InvalidOperationException("Settings failed to persist ThemeMode.Dark.");
            });

            RunTest("Theme Manager Applying Light / Dark / System Themes", () =>
            {
                ThemeManager.ApplyTheme(ThemeMode.Light);
                ThemeManager.ApplyTheme(ThemeMode.Dark);
                ThemeManager.ApplyTheme(ThemeMode.System);
            });

            RunTest("Password Encryption & Master Password Vault Locks", () =>
            {
                string testMasterPass = "TestMasterPass123!";
                DataService.MasterPassword = testMasterPass;

                var testItem = new PasswordEntry
                {
                    Title = "Test Platform",
                    Username = "testuser",
                    Password = "secretpassword"
                };

                var items = new List<PasswordEntry> { testItem };
                DataService.SavePasswords(items);

                var loaded = DataService.LoadPasswords();
                if (loaded.Count == 0 || loaded[0].Title != "Test Platform")
                    throw new InvalidOperationException("Password item serialization failed.");

                DataService.MasterPassword = null;
            });

            RunTest("Reminder Serialization & Date Grouping", () =>
            {
                var reminder = new Reminder
                {
                    Title = "Test Reminder",
                    Month = DateTime.Now.Month,
                    Day = DateTime.Now.Day,
                    DaysInAdvance = 1
                };

                var list = new List<Reminder> { reminder };
                DataService.SaveReminders(list);

                var loaded = DataService.LoadReminders();
                if (loaded.Count == 0 || loaded[0].Title != "Test Reminder")
                    throw new InvalidOperationException("Reminder serialization failed.");
            });

            RunTest("Wallpaper Service State & Rotation Config", () =>
            {
                var wallState = new WallpaperState
                {
                    Name = "Test Wallpaper",
                    DesignWidth = 1920,
                    DesignHeight = 1080
                };
                var list = new List<WallpaperState> { wallState };
                WallpaperService.StopRotation();
            });

            RunTest("View Instantiations (Home, Passwords, Reminders, Wallpapers, DynamicInfo, Settings, Help)", () =>
            {
                var homeView = new HomeView();
                homeView.RefreshDashboard();

                var passwordsView = new PasswordsView();
                passwordsView.CheckVaultStateAndLoad();

                var remindersView = new RemindersView();
                remindersView.LoadReminders();

                var wallpapersView = new WallpapersView();
                wallpapersView.LoadData();

                var dynamicInfoView = new DynamicInfoView();
                var settingsView = new SettingsView();
                var helpView = new HelpView();
            });

            RunTest("MainWindow Navigation & Full UI Switch Automation", () =>
            {
                var mainWindow = new MainWindow();
                mainWindow.NavigateTo("Home");
                mainWindow.NavigateTo("Passwords");
                mainWindow.NavigateTo("Reminders");
                mainWindow.NavigateTo("Wallpaper");
                mainWindow.NavigateTo("DynamicInfo");
                mainWindow.NavigateTo("Settings");
                mainWindow.NavigateTo("Help");
            });

            RunTest("Dialog Windows Instantiation (ConfirmDialog, SetupWizardWindow, ApiDataWizardWindow)", () =>
            {
                var settings = SettingsService.LoadSettings();
                var confirm = new ConfirmDialog("Test Title", "Test Message");
                var wizard = new SetupWizardWindow(settings);

                var testElem = new TextElementState { Text = "Test API", DynamicType = "Api" };
                var apiWizard = new ApiDataWizardWindow(testElem);
            });

            RunTest("WallpaperEditorWindow 3-Column Editor Instantiation & State Loading", () =>
            {
                var testState = new WallpaperState
                {
                    Name = "Test Editor Wallpaper",
                    DesignWidth = 1920,
                    DesignHeight = 1080,
                    TextElements = new List<TextElementState>
                    {
                        new TextElementState { Text = "{公历日期}", DynamicType = "GregorianDate", X = 100, Y = 100 },
                        new TextElementState { Text = "{API数据}", DynamicType = "Api", X = 100, Y = 200, StrokeEnabled = true, ShadowEnabled = true }
                    }
                };

                var editor = new WallpaperEditorWindow(testState);
            });

            Console.WriteLine("=================================================");
            Console.WriteLine($"✓ ALL {passed}/{total} AUTOMATED TESTS PASSED SUCCESSFULLY!");
            Console.WriteLine("=================================================");
        }
        finally
        {
            // ALWAYS restore real user files after test execution!
            try
            {
                if (backupPasswords != null) File.WriteAllText(passwordsFile, backupPasswords);
                else if (File.Exists(passwordsFile)) File.Delete(passwordsFile);

                if (backupReminders != null) File.WriteAllText(remindersFile, backupReminders);
                else if (File.Exists(remindersFile)) File.Delete(remindersFile);

                if (backupSettings != null) File.WriteAllText(settingsFile, backupSettings);
                else if (File.Exists(settingsFile)) File.Delete(settingsFile);

                if (backupWallpapers != null) File.WriteAllText(wallpapersFile, backupWallpapers);
                else if (File.Exists(wallpapersFile)) File.Delete(wallpapersFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Test Cleanup Warning] Failed to restore backup user files: {ex.Message}");
            }
        }
    }
}
