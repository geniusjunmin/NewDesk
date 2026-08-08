using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NewDesk.Dialogs;
using NewDesk.Models;
using NewDesk.Models.Ai;
using NewDesk.Services.Ai;
using NewDesk.Services.Ai.Tools;
using NewDesk.Services.Security;
using NewDesk.Models.Security;
using NewDesk.Views;
using ThemeMode = NewDesk.Models.ThemeMode;

namespace NewDesk.Services;

public static class AutomatedTestRunner
{
    public static async Task<int> RunAllTestsAsync()
    {
        string testGuid = Guid.NewGuid().ToString("N");
        string testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskTests_{testGuid}");

        Console.WriteLine("=================================================");
        Console.WriteLine("⚡ NewDesk Automated Test Suite Execution");
        Console.WriteLine($"TEST DATA ROOT: {testRoot}");
        Console.WriteLine("REAL APP DATA WILL NOT BE TOUCHED OR MODIFIED.");
        Console.WriteLine("=================================================");

        AppEnvironment.SetTestEnvironment(testRoot);

        int passed = 0;
        int total = 0;
        bool hasFailure = false;

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
                hasFailure = true;
                Console.WriteLine("FAILED ✗");
                Console.WriteLine($"  Error Details: {ex.Message}");
                Console.WriteLine($"  Stack Trace: {ex.StackTrace}");
            }
        }

        async Task RunTestAsync(string testName, Func<Task> testActionAsync)
        {
            total++;
            try
            {
                Console.Write($"[TEST {total}] {testName}... ");
                await testActionAsync();
                Console.WriteLine("PASSED ✓");
                passed++;
            }
            catch (Exception ex)
            {
                hasFailure = true;
                Console.WriteLine("FAILED ✗");
                Console.WriteLine($"  Error Details: {ex.Message}");
                Console.WriteLine($"  Stack Trace: {ex.StackTrace}");
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

                AiSecretStorageService.SaveSecret(testSecretId, testSecretValue);
                string? retrieved = AiSecretStorageService.GetSecret(testSecretId);
                if (retrieved != testSecretValue)
                    throw new InvalidOperationException("DPAPI secret retrieval failed.");

                AiSecretStorageService.DeleteSecret(testSecretId);
                if (AiSecretStorageService.GetSecret(testSecretId) != null)
                    throw new InvalidOperationException("DPAPI secret deletion failed.");

                string rawLog = "Header Bearer sk-1234567890abcdef1234567890abcdef and password=mysecretpass";
                string redacted = SecretRedactor.Redact(rawLog);
                if (redacted.Contains("sk-1234567890abcdef1234567890abcdef") || redacted.Contains("mysecretpass"))
                    throw new InvalidOperationException("SecretRedactor failed to censor tokens.");
            });

            await RunTestAsync("AI Outbound Prompt Redaction Test (LOCAL STORAGE != OUTBOUND PAYLOAD)", async () =>
            {
                string capturedBody = "";
                var mockHandler = new MockHttpMessageHandler(req =>
                {
                    capturedBody = req.Content?.ReadAsStringAsync().Result ?? "";
                    return MockHttpMessageHandler.CreateJsonResponse("{\"choices\":[{\"message\":{\"content\":\"Processed\"}}]}");
                });

                var mockClient = new HttpClient(mockHandler);
                var config = new AiProviderConfig
                {
                    BaseUrl = "https://mock.api.com/v1",
                    Protocol = AiApiProtocol.ChatCompletions,
                    SelectedModel = "mock-model"
                };

                var provider = new OpenAiCompatibleProvider(config, mockClient);
                string rawUserPrompt = "My password=SuperSecret123 and token=sk-1234567890abcdef";

                var outboundMsgs = AiOutboundContextBuilder.BuildMessages(new List<AiMessage>(), rawUserPrompt);
                var aiReq = new AiRequest { Messages = outboundMsgs };

                var resp = await provider.CompleteAsync(aiReq);

                if (capturedBody.Contains("SuperSecret123") || capturedBody.Contains("sk-1234567890abcdef"))
                    throw new InvalidOperationException("Outbound Prompt Redaction failed! Raw secret was leaked to HTTP payload!");
            });

            await RunTestAsync("AI Provider SSE Streaming & Mock Parsing Test", async () =>
            {
                var mockHandler = new MockHttpMessageHandler(req =>
                {
                    var lines = new[]
                    {
                        "data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}",
                        "data: {\"choices\":[{\"delta\":{\"content\":\"World!\"}}]}",
                        "data: [DONE]"
                    };
                    return MockHttpMessageHandler.CreateSseResponse(lines);
                });

                var mockClient = new HttpClient(mockHandler);
                var config = new AiProviderConfig
                {
                    BaseUrl = "https://mock.api.com/v1",
                    Protocol = AiApiProtocol.ChatCompletions,
                    SelectedModel = "mock-model"
                };

                var provider = new OpenAiCompatibleProvider(config, mockClient);
                var request = new AiRequest { Messages = new List<AiMessage> { new AiMessage { Role = "user", Content = "Hi" } } };

                string result = "";
                await foreach (var chunk in provider.StreamAsync(request))
                {
                    if (!string.IsNullOrEmpty(chunk.TextDelta))
                    {
                        result += chunk.TextDelta;
                    }
                }

                if (result != "Hello World!")
                    throw new InvalidOperationException($"Mock SSE streaming failed. Result: {result}");
            });

            RunTest("NaturalLanguageReminderParser relative keyword order (大后天 vs 后天)", () =>
            {
                var now = new DateTime(2026, 8, 8, 10, 0, 0);
                var clock = new TestClock(now);

                var r3 = NaturalLanguageReminderParser.Parse("大后天下午3点开会", clock);
                if (r3.Title != "开会" || r3.DueAt?.Date != new DateTime(2026, 8, 11))
                    throw new InvalidOperationException($"Parsing '大后天' failed: expected 2026-08-11, got {r3.DueAt}");

                var r2 = NaturalLanguageReminderParser.Parse("后天下午3点开会", clock);
                if (r2.Title != "开会" || r2.DueAt?.Date != new DateTime(2026, 8, 10))
                    throw new InvalidOperationException($"Parsing '后天' failed: expected 2026-08-10, got {r2.DueAt}");
            });

            RunTest("NaturalLanguageReminderParser TryCreateDate & invalid month/day checks", () =>
            {
                var clock = new TestClock(new DateTime(2026, 8, 8));

                try
                {
                    NaturalLanguageReminderParser.Parse("2月31日开会", clock);
                    throw new InvalidOperationException("Parser failed to throw exception on invalid 2月31日!");
                }
                catch (ArgumentException ex) when (ex.Message.Contains("最多只有"))
                {
                    // Expected behavior
                }

                if (NaturalLanguageReminderParser.TryCreateDate(2026, 2, 31, out _))
                    throw new InvalidOperationException("TryCreateDate returned true for 2026-02-31!");
            });

            RunTest("ReminderScheduleCalculator OneTime, Yearly, LunarYearly Next Occurrence", () =>
            {
                var now = new DateTime(2026, 8, 8, 10, 0, 0);

                var yearly = new Reminder { ScheduleType = ReminderScheduleType.Yearly, Month = 8, Day = 10, TimeOfDay = new TimeSpan(9, 0, 0) };
                var nextYearly = ReminderScheduleCalculator.GetNextOccurrence(yearly, now);
                if (nextYearly?.Date != new DateTime(2026, 8, 10))
                    throw new InvalidOperationException($"Yearly next occurrence expected 2026-08-10, got {nextYearly}");

                var pastYearly = new Reminder { ScheduleType = ReminderScheduleType.Yearly, Month = 8, Day = 5, TimeOfDay = new TimeSpan(9, 0, 0) };
                var nextPastYearly = ReminderScheduleCalculator.GetNextOccurrence(pastYearly, now);
                if (nextPastYearly?.Date != new DateTime(2027, 8, 5))
                    throw new InvalidOperationException($"Past yearly next occurrence expected 2027-08-05, got {nextPastYearly}");
            });

            RunTest("RemindersV1ToV2MigrationStep Raw JSON Inspection (Legacy Annual Reminder)", () =>
            {
                string remindersPath = AppDataPath.RemindersFile;
                string legacyJson = "[{\"Id\":\"" + Guid.NewGuid() + "\",\"Title\":\"Old Birthday\",\"Month\":10,\"Day\":15,\"IsLunar\":false,\"DaysInAdvance\":1}]";
                File.WriteAllText(remindersPath, legacyJson);

                var step = new RemindersV1ToV2MigrationStep();
                bool ok = step.Execute();
                if (!ok) throw new InvalidOperationException("RemindersV1ToV2MigrationStep execution failed.");

                var loaded = DataService.LoadReminders();
                if (loaded.Count == 0 || loaded[0].ScheduleType != ReminderScheduleType.Yearly || loaded[0].DueAt != null)
                    throw new InvalidOperationException($"Legacy reminder migration failed! ScheduleType: {loaded[0].ScheduleType}, DueAt: {loaded[0].DueAt}");
            });

            RunTest("BackupService ZIP Creation & Restore Verification Test", () =>
            {
                string backupZipPath = Path.Combine(testRoot, "test_backup.ndbackup");
                BackupService.CreateBackup(backupZipPath);

                if (!File.Exists(backupZipPath))
                    throw new InvalidOperationException("BackupService failed to create target backup ZIP file!");

                bool restored = BackupService.RestoreBackup(backupZipPath);
                if (!restored)
                    throw new InvalidOperationException("BackupService failed to restore backup ZIP!");
            });

            Console.WriteLine("=================================================");
            if (hasFailure)
            {
                Console.WriteLine($"❌ TEST SUITE FAILED: {passed}/{total} PASSED.");
                Console.WriteLine("=================================================");
                Environment.ExitCode = 1;
                return 1;
            }
            else
            {
                Console.WriteLine($"✓ ALL {passed}/{total} AUTOMATED TESTS PASSED SUCCESSFULLY!");
                Console.WriteLine("=================================================");
                return 0;
            }
        }
        finally
        {
            AppEnvironment.ResetToNormalEnvironment();
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
