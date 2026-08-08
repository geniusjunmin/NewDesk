using System.IO;
using System.Net;
using System.Net.Http;
using NewDesk.Models.Ai;
using NewDesk.Services;
using NewDesk.Services.Ai;
using NewDesk.Services.Security;
using Xunit;

namespace NewDesk.Tests;

public sealed class ProviderPersistenceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskProviderTests_{Guid.NewGuid():N}");

    public ProviderPersistenceTests()
    {
        AppEnvironment.SetTestEnvironment(_testRoot);
        AiProviderRegistry.ResetForTesting();
    }

    public void Dispose()
    {
        AiProviderRegistry.ResetForTesting();
        AppEnvironment.ResetToNormalEnvironment();
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [Fact]
    public void NewProvider_DefaultModelEmpty() => Assert.Equal(string.Empty, new AiProviderConfig().SelectedModel);

    [Fact]
    public void NoApiKey_DoesNotCreateSecretId()
    {
        var config = new AiProviderConfig();
        AiProviderSecretPolicy.Apply(config, "", clearExistingSecret: false);
        Assert.Null(config.SecretId);
    }

    [Fact]
    public void DisabledNoKeyProvider_SurvivesReload()
    {
        var config = new AiProviderConfig
        {
            ProviderId = "disabled-provider",
            Name = "Disabled",
            BaseUrl = "https://example.test/v1",
            IsEnabled = false,
            SecretId = null
        };
        AiProviderRegistry.SaveProvider(config);
        AiProviderRegistry.ResetForTesting();

        Assert.Contains(AiProviderRegistry.GetAllProviders(), provider => provider.ProviderId == config.ProviderId);
    }

    [Fact]
    public void CustomResponses_DefaultToolsFalse()
    {
        var provider = new ResponsesApiProvider(new AiProviderConfig
        {
            Kind = AiProviderKind.Custom,
            Protocol = AiApiProtocol.Responses,
            BaseUrl = "https://example.test/v1"
        });
        Assert.False(provider.Capabilities.SupportsTools);
        Assert.False(provider.Capabilities.SupportsVision);
        Assert.False(provider.Capabilities.SupportsStructuredOutput);
    }

    [Fact]
    public async Task RemoteHttpSecret_RequestCountZero()
    {
        const string secretId = "provider-http-secret";
        SecretStorageService.SaveSecret(secretId, "secret");
        var handler = new CountingHandler();
        var provider = new ResponsesApiProvider(new AiProviderConfig
        {
            BaseUrl = "http://example.com/v1",
            SecretId = secretId
        }, new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetModelsAsync());
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
