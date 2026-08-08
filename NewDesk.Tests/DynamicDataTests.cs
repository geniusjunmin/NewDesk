using System.IO;
using System.Net;
using System.Net.Http;
using NewDesk.Models;
using NewDesk.Services;
using NewDesk.Services.Security;
using Xunit;

namespace NewDesk.Tests;

public sealed class DynamicDataTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskDynamicDataTests_{Guid.NewGuid():N}");

    public DynamicDataTests() => AppEnvironment.SetTestEnvironment(_testRoot);

    public void Dispose()
    {
        DynamicDataService.HttpSenderOverride = null;
        AppEnvironment.ResetToNormalEnvironment();
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [Fact]
    public void JsonPath_TestThenSave_PreservesExtractionType()
    {
        var source = new DynamicDataSource
        {
            Name = "JSON",
            Url = "https://example.test/data",
            ExtractionType = "JsonPath",
            ExtractionRule = "$.weather.temperature"
        };

        Assert.True(DynamicDataService.SaveSources([source]).IsSuccess);
        var reloaded = DynamicDataService.LoadSources().Single(item => item.Id == source.Id);

        Assert.Equal("JsonPath", reloaded.ExtractionType);
        Assert.Equal("$.weather.temperature", reloaded.ExtractionRule);
    }

    [Fact]
    public async Task TestSource_UsesOneHttpRequest()
    {
        int requestCount = 0;
        DynamicDataService.HttpSenderOverride = _ =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse("{\"temperature\":\"18\"}"));
        };
        var source = JsonSource();

        var result = await DynamicDataService.TestSourceAsync(source);

        Assert.True(result.Success);
        Assert.Equal("18", result.Value);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public void JsonPath_SelectedTreeNode_SavesFullPath()
    {
        const string raw = "{\"weather\":{\"daily\":[{\"temperature\":18}]}}";
        Assert.True(JsonPathExtractor.TryExtract(raw, "$.weather.daily[0].temperature", out var value));
        Assert.Equal("18", value);
    }

    [Fact]
    public async Task TestSource_Error_NotReportedSuccess()
    {
        DynamicDataService.HttpSenderOverride = _ => Task.FromResult(JsonResponse("{\"other\":1}"));
        var result = await DynamicDataService.TestSourceAsync(JsonSource());

        Assert.False(result.Success);
        Assert.Contains("JsonPath", result.Error);
    }

    [Fact]
    public async Task SecretHeader_TestUsesSecretHeader()
    {
        const string secretId = "dynamic_test_auth";
        SecretStorageService.SaveSecret(secretId, "Bearer test-token");
        string? capturedAuthorization = null;
        DynamicDataService.HttpSenderOverride = request =>
        {
            capturedAuthorization = string.Join(" ", request.Headers.GetValues("Authorization"));
            return Task.FromResult(JsonResponse("{\"temperature\":\"18\"}"));
        };
        var source = JsonSource();
        source.SecretHeaders["Authorization"] = secretId;

        var result = await DynamicDataService.TestSourceAsync(source);

        Assert.True(result.Success);
        Assert.Equal("Bearer test-token", capturedAuthorization);
    }

    private static DynamicDataSource JsonSource() => new()
    {
        Name = "weather",
        Url = "https://example.test/data",
        ExtractionType = "JsonPath",
        ExtractionRule = "$.temperature"
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json)
    };
}
