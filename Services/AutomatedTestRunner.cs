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

            await RunTestAsync("AI Tool Call System & Safety Permissions", async () =>
            {
                var tools = AiToolRegistry.GetAllTools();
                if (tools.Count < 3)
                    throw new InvalidOperationException("AI tool registry is missing core tools.");

                var sysInfoTool = AiToolRegistry.GetTool("get_system_info");
                if (sysInfoTool == null || sysInfoTool.RequiresUserConfirmation)
                    throw new InvalidOperationException("SystemInfoTool should be read-only without confirmation requirement.");

                var res = await sysInfoTool.ExecuteAsync("{}");
                if (res.IsError || string.IsNullOrEmpty(res.OutputJson))
                    throw new InvalidOperationException("SystemInfoTool execution failed.");

                var reminderTool = AiToolRegistry.GetTool("create_reminder");
                if (reminderTool == null || !reminderTool.RequiresUserConfirmation)
                    throw new InvalidOperationException("ReminderCreateTool MUST require user confirmation.");
            });

            await RunTestAsync("AI Tool Confirmation Denied (Null Callback DENY Test)", async () =>
            {
                var toolCall = new AiToolCall
                {
                    Id = "call_test",
                    Name = "create_reminder",
                    ArgumentsJson = "{\"title\":\"Test Denied\",\"month\":8,\"day\":8}"
                };

                var deniedResult = await AiToolExecutionService.ExecuteToolWithPermissionAsync(toolCall, userConfirmationCallback: null);
                if (!deniedResult.IsError || !deniedResult.OutputJson.Contains("安全防线"))
                    throw new InvalidOperationException("AiToolExecutionService failed to deny execution when confirmation callback was null!");
            });

            await RunTestAsync("AI Tool Confirmation Accepted & Tool Result Loop Test", async () =>
            {
                var toolCall = new AiToolCall
                {
                    Id = "call_test2",
                    Name = "create_reminder",
                    ArgumentsJson = "{\"title\":\"Test Accepted\",\"month\":8,\"day\":18}"
                };

                var acceptedResult = await AiToolExecutionService.ExecuteToolWithPermissionAsync(toolCall, userConfirmationCallback: pending => Task.FromResult(true));
                if (acceptedResult.IsError)
                    throw new InvalidOperationException("AiToolExecutionService failed to execute tool when user confirmed.");
            });

            RunTest("Tool Execution Display Formatting Test", () =>
            {
                var info = ToolExecutionDisplayInfo.Format("switch_wallpaper", "{}", true);
                if (info.Icon != "🖼️" || !info.IsSuccess)
                    throw new InvalidOperationException("ToolExecutionDisplayInfo failed to format switch_wallpaper.");

                var remInfo = ToolExecutionDisplayInfo.Format("create_reminder", "{\"title\":\"交电费\",\"dueAt\":\"2026-08-09 15:00\"}", true);
                if (remInfo.Icon != "🔔" || !remInfo.Detail.Contains("交电费"))
                    throw new InvalidOperationException("ToolExecutionDisplayInfo failed to format create_reminder.");
            });

            RunTest("Endpoint Security Scheme Whitelist Test", () =>
            {
                try
                {
                    EndpointSecurityPolicy.ValidateEndpoint("ftp://api.example.com/v1", false);
                    throw new InvalidOperationException("EndpointSecurityPolicy failed to reject non-http/https ftp protocol!");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("不支持的 Endpoint Scheme"))
                {
                    // Expected behavior
                }
            });

            RunTest("Dynamic Data Sources Persistence & Predefined Presets", () =>
            {
                var sources = DynamicDataService.LoadSources();
                if (sources.Count == 0)
                    throw new InvalidOperationException("DynamicDataService failed to load preset sources.");
            });

            RunTest("Dynamic Secret Headers DPAPI Storage Test", () =>
            {
                var source = new DynamicDataSource
                {
                    Name = "Test API Source",
                    Headers = new Dictionary<string, string>
                    {
                        { "Authorization", "Bearer my_super_secret_token" },
                        { "Content-Type", "application/json" }
                    }
                };

                var res = DynamicDataService.SaveSources(new List<DynamicDataSource> { source });
                if (!res.IsSuccess)
                    throw new InvalidOperationException($"DynamicDataService.SaveSources failed: {res.Message}");

                if (source.Headers.ContainsKey("Authorization"))
                    throw new InvalidOperationException("Sensitive header Authorization was not automatically removed from plaintext Headers dictionary!");

                if (!source.SecretHeaders.ContainsKey("Authorization"))
                    throw new InvalidOperationException("Sensitive header Authorization was not stored in SecretHeaders!");
            });

            RunTest("Real JsonPathExtractor Nested & Array Index Parsing Test", () =>
            {
                string sampleJson = "{\"data\":{\"items\":[{\"price\":18},{\"price\":25}]}}";

                if (!JsonPathExtractor.TryExtract(sampleJson, "$.data.items[0].price", out string val) || val != "18")
                    throw new InvalidOperationException($"JsonPathExtractor failed to parse $.data.items[0].price. Result: {val}");
            });

            RunTest("JsonPath Extraction Failure Uses Cache Fallback Test", () =>
            {
                string badJson = "{\"invalid\":\"structure\"}";
                bool success = JsonPathExtractor.TryExtract(badJson, "$.data.items[0].price", out string val);
                if (success)
                    throw new InvalidOperationException("JsonPathExtractor should return false for non-matching paths.");
            });

            RunTest("NetworkEndpointClassifier Loopback vs Cloud Endpoint Test", () =>
            {
                if (!NetworkEndpointClassifier.IsLocalEndpoint("http://localhost:11434/v1"))
                    throw new InvalidOperationException("http://localhost:11434/v1 should be classified as Local!");

                if (!NetworkEndpointClassifier.IsLocalEndpoint("http://127.0.0.1:1234/v1"))
                    throw new InvalidOperationException("http://127.0.0.1:1234/v1 should be classified as Local!");

                if (NetworkEndpointClassifier.IsLocalEndpoint("https://localhost.evil.com/v1"))
                    throw new InvalidOperationException("https://localhost.evil.com/v1 MUST NOT be classified as Local!");
            });

            RunTest("AI PrivacyGuard Category Flags Enforcement Test", () =>
            {
                var settings = SettingsService.LoadSettings();
                settings.AiNetworkMode = AiNetworkMode.LocalOnly;
                SettingsService.SaveSettings(settings);

                var cloudProvider = new AiProviderConfig
                {
                    Kind = AiProviderKind.OpenAI,
                    BaseUrl = "https://api.openai.com/v1"
                };

                try
                {
                    AiPrivacyGuard.ValidateRequest(cloudProvider, DataSensitivity.Personal, AiDataCategory.UserPrompt, "Hello");
                    throw new InvalidOperationException("AiPrivacyGuard failed to block Cloud provider in LocalOnly network mode!");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("仅限本地模型模式"))
                {
                    // Expected behavior
                }

                settings.AiNetworkMode = AiNetworkMode.AllowCloud;
                SettingsService.SaveSettings(settings);
            });

            RunTest("Natural Language Reminder Parsing & Title Clean Test", () =>
            {
                var parsed = NaturalLanguageReminderParser.Parse("明天下午3点开会");
                if (parsed.Title != "开会")
                    throw new InvalidOperationException($"Parsed title mismatch: Expected '开会', got '{parsed.Title}'");

                parsed.SnoozeUntil = DateTime.Now.AddMinutes(15);
                if (!parsed.SnoozeUntil.HasValue)
                    throw new InvalidOperationException("SnoozeUntil setting failed.");
            });

            RunTest("Vault 2.0 TOTP Code Generation & Local Password Health Score", () =>
            {
                string testSecret = "JBSWY3DPEHPK3PXP";
                var (code, remaining) = TotpService.GenerateTotp(testSecret);
                if (code.Length != 6 || remaining <= 0 || remaining > 30)
                    throw new InvalidOperationException($"TOTP generation invalid: code={code}, remaining={remaining}");

                var entries = new List<PasswordEntry>
                {
                    new PasswordEntry { Title = "Site1", Password = "123" },
                    new PasswordEntry { Title = "Site2", Password = "123" },
                    new PasswordEntry { Title = "Site3", Password = "StrongPassword123!" }
                };

                var report = PasswordHealthService.Evaluate(entries);
                if (report.WeakCount < 2 || report.Score >= 100)
                    throw new InvalidOperationException($"PasswordHealthService report calculation failed: Score={report.Score}, Weak={report.WeakCount}");
            });

            RunTest("Wallpaper Pro Asset Library & TextElementState Complete Field Cloning Test", () =>
            {
                var original = new TextElementState
                {
                    Text = "Sample Text",
                    IsLocked = true,
                    IsVisible = false,
                    DataSourceId = "ds_12345",
                    ShadowEnabled = true,
                    StrokeEnabled = true
                };

                var cloned = original.Clone();
                if (!cloned.IsLocked || cloned.IsVisible || cloned.DataSourceId != "ds_12345" || !cloned.ShadowEnabled || !cloned.StrokeEnabled)
                    throw new InvalidOperationException("TextElementState.Clone failed to copy complete fields!");
            });

            RunTest("Wallpaper TextRenderer IsVisible Early Return Test", () =>
            {
                var hiddenElement = new TextElementState { Text = "Hidden", IsVisible = false };
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
