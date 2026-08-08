using NewDesk.Models;
using System.IO;
using NewDesk.Models.Ai;
using NewDesk.Services;
using NewDesk.Services.Ai;
using NewDesk.Services.Ai.Tools;
using Xunit;

namespace NewDesk.Tests;

public sealed class AiToolPrivacyTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskAiPrivacyTests_{Guid.NewGuid():N}");

    public AiToolPrivacyTests()
    {
        AppEnvironment.SetTestEnvironment(_testRoot);
    }

    public void Dispose()
    {
        AppEnvironment.ResetToNormalEnvironment();
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [Fact]
    public void Clipboard_DoesNotReceiveTools()
    {
        AssertToolsDisabled("Clipboard");
    }

    [Fact]
    public void DailyBrief_DoesNotReceiveTools()
    {
        AssertToolsDisabled("DailyBrief");
    }

    [Fact]
    public void DynamicAiText_DoesNotReceiveTools()
    {
        AssertToolsDisabled("DynamicAiText");
    }

    [Fact]
    public void Assistant_OnlyReceivesAllowedTools()
    {
        var request = new AiTurnRequest
        {
            EnableTools = true,
            AllowedToolNames = new(StringComparer.OrdinalIgnoreCase) { "create_reminder" }
        };

        var definitions = AiOrchestrator.GetAllowedToolDefinitions(request, providerSupportsTools: true);

        Assert.Collection(definitions, definition => Assert.Equal("create_reminder", definition.Name));
    }

    [Fact]
    public void UnauthorizedReturnedTool_NotExecuted()
    {
        var request = new AiTurnRequest
        {
            EnableTools = true,
            AllowedToolNames = new(StringComparer.OrdinalIgnoreCase) { "switch_wallpaper" }
        };

        Assert.False(AiOrchestrator.IsToolAllowed(request, "get_system_info"));
    }

    [Fact]
    public async Task SystemInfoCloud_RequiresExplicitConsent()
    {
        SettingsService.SaveSettings(new AppSettings { AllowAiSystemInfo = true });
        CloudSendPreview? captured = null;
        var request = new AiTurnRequest
        {
            CloudConsentCallback = preview =>
            {
                captured = preview;
                return Task.FromResult(false);
            }
        };

        var result = await ExecuteCloudSystemInfoAsync(request);

        Assert.True(result.IsError);
        Assert.Contains("system_info_cloud_consent_denied", result.OutputJson);
        Assert.NotNull(captured);
        Assert.True(captured!.IncludesSystemInfo);
        Assert.Contains("MachineName", captured.SanitizedPreview);
    }

    [Fact]
    public async Task SystemInfoResult_NotSentWithoutConsent()
    {
        SettingsService.SaveSettings(new AppSettings { AllowAiSystemInfo = true });
        var request = new AiTurnRequest { CloudConsentCallback = _ => Task.FromResult(false) };

        var result = await ExecuteCloudSystemInfoAsync(request);

        Assert.True(result.IsError);
        Assert.DoesNotContain(Environment.MachineName, result.OutputJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessorCount", result.OutputJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SystemInfoLocal_RequiresSetting()
    {
        SettingsService.SaveSettings(new AppSettings { AllowAiSystemInfo = false });
        var result = await new SystemInfoTool().ExecuteAsync("{}");
        Assert.True(result.IsError);
        Assert.Contains("system_info_not_allowed", result.OutputJson);
    }

    private static Task<AiToolResult> ExecuteCloudSystemInfoAsync(AiTurnRequest request)
    {
        return AiOrchestrator.ExecuteSystemInfoToolAsync(
            new AiToolCall { Id = "call-1", Name = "get_system_info", ArgumentsJson = "{}" },
            request,
            new AiProviderConfig
            {
                Name = "Cloud",
                BaseUrl = "https://example.com/v1",
                SelectedModel = "test-model"
            },
            isLocal: false,
            AiDataCategory.UserPrompt,
            "system",
            [],
            "prompt");
    }

    private static void AssertToolsDisabled(string entryPoint)
    {
        var request = new AiTurnRequest { EnableTools = false, UserPrompt = entryPoint };
        Assert.Empty(AiOrchestrator.GetAllowedToolDefinitions(request, providerSupportsTools: true));
    }
}
