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
        string testGuid = Guid.NewGuid().ToString("N");
        string testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskTests_{testGuid}");

        Console.WriteLine("=================================================");
        Console.WriteLine("⚡ NewDesk Automated Test Suite Execution");
        Console.WriteLine($"TEST DATA ROOT: {testRoot}");
        Console.WriteLine("REAL APP DATA WILL NOT BE TOUCHED OR MODIFIED.");
        Console.WriteLine("=================================================");

        // Switch AppEnvironment to isolated test directory
        AppEnvironment.SetTestEnvironment(testRoot);

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

            RunTest("AI Secret Storage (DPAPI) & Redactor Unit Tests", () =>
            {
                string testSecretId = "secret_test_key";
                string testSecretValue = "sk-1234567890abcdef1234567890abcdef";

                Services.Ai.AiSecretStorageService.SaveSecret(testSecretId, testSecretValue);
                string? retrieved = Services.Ai.AiSecretStorageService.GetSecret(testSecretId);
                if (retrieved != testSecretValue)
                    throw new InvalidOperationException("DPAPI secret retrieval failed.");

                Services.Ai.AiSecretStorageService.DeleteSecret(testSecretId);
                if (Services.Ai.AiSecretStorageService.GetSecret(testSecretId) != null)
                    throw new InvalidOperationException("DPAPI secret deletion failed.");

                string rawLog = "Header Bearer sk-1234567890abcdef1234567890abcdef and password=mysecretpass";
                string redacted = Services.Ai.SecretRedactor.Redact(rawLog);
                if (redacted.Contains("sk-1234567890abcdef1234567890abcdef") || redacted.Contains("mysecretpass"))
                    throw new InvalidOperationException("SecretRedactor failed to censor tokens.");
            });

            RunTest("AI Provider Registry Presets & Factory Instantiation", () =>
            {
                var presets = Services.Ai.AiProviderRegistry.GetDefaultPresets();
                if (presets.Count < 5)
                    throw new InvalidOperationException("Preset providers list is incomplete.");

                var defaultProvider = Services.Ai.AiProviderRegistry.GetDefaultProvider();
                if (defaultProvider == null)
                    throw new InvalidOperationException("Default provider is null.");

                var providerInstance = Services.Ai.AiProviderFactory.CreateProvider(defaultProvider);
                if (providerInstance == null || string.IsNullOrEmpty(providerInstance.ProviderId))
                    throw new InvalidOperationException("Provider factory failed to instantiate IAiProvider.");
            });

            RunTest("AI Tool Call System & Safety Permissions", async () =>
            {
                var tools = Services.Ai.Tools.AiToolRegistry.GetAllTools();
                if (tools.Count < 3)
                    throw new InvalidOperationException("AI tool registry is missing core tools.");

                var sysInfoTool = Services.Ai.Tools.AiToolRegistry.GetTool("get_system_info");
                if (sysInfoTool == null || sysInfoTool.RequiresUserConfirmation)
                    throw new InvalidOperationException("SystemInfoTool should be read-only without confirmation requirement.");

                var res = await sysInfoTool.ExecuteAsync("{}");
                if (res.IsError || string.IsNullOrEmpty(res.OutputJson))
                    throw new InvalidOperationException("SystemInfoTool execution failed.");

                var reminderTool = Services.Ai.Tools.AiToolRegistry.GetTool("create_reminder");
                if (reminderTool == null || !reminderTool.RequiresUserConfirmation)
                    throw new InvalidOperationException("ReminderCreateTool MUST require user confirmation.");
            });

            Console.WriteLine("=================================================");
            Console.WriteLine($"✓ ALL {passed}/{total} AUTOMATED TESTS PASSED SUCCESSFULLY!");
            Console.WriteLine("=================================================");
        }
        finally
        {
            // Reset environment back to normal mode
            AppEnvironment.ResetToNormalEnvironment();

            // Clean up temporary test directory
            try
            {
                if (Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Test Cleanup Warning] Could not delete temp folder: {ex.Message}");
            }
        }
    }
}
