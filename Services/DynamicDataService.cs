using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NewDesk.Models;
using NewDesk.Models.Ai;
using NewDesk.Models.Security;
using NewDesk.Services.Ai;
using NewDesk.Services.Security;

namespace NewDesk.Services;

public class OperationResult
{
    public bool IsSuccess { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }

    public static OperationResult Success(string msg = "") => new OperationResult { IsSuccess = true, Message = msg };
    public static OperationResult Fail(string msg, Exception? ex = null) => new OperationResult { IsSuccess = false, Message = msg, Exception = ex };
}

public class OperationResult<T> : OperationResult
{
    public T? Value { get; set; }
    public static OperationResult<T> Success(T value, string message = "") => new() { IsSuccess = true, Value = value, Message = message };
    public new static OperationResult<T> Fail(string message, Exception? ex = null) => new() { IsSuccess = false, Message = message, Exception = ex };
}

public class ExtractionResult
{
    public bool IsSuccess { get; set; }
    public string Value { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;

    public static ExtractionResult Success(string val) => new ExtractionResult { IsSuccess = true, Value = val };
    public static ExtractionResult Fail(string msg) => new ExtractionResult { IsSuccess = false, ErrorMessage = msg };
}

public sealed class DynamicDataTestResult
{
    public bool Success { get; init; }
    public string Value { get; init; } = string.Empty;
    public string RawContent { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

public static class DynamicDataService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    internal static Func<HttpRequestMessage, Task<HttpResponseMessage>>? HttpSenderOverride { get; set; }
    private static List<DynamicDataSource> _sources = new();

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "X-API-Key", "Api-Key", "X-Auth-Token"
    };

    public static List<DynamicDataSource> LoadSources()
    {
        try
        {
            string path = AppDataPath.DynamicDataFile;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                _sources = JsonSerializer.Deserialize<List<DynamicDataSource>>(json) ?? GetDefaultPresets();
            }
            else
            {
                _sources = GetDefaultPresets();
                SaveSources(_sources);
            }

            MigrateSensitiveHeaders();
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("DynamicDataService.LoadSources", ex);
            _sources = GetDefaultPresets();
        }
        return _sources;
    }

    private static void MigrateSensitiveHeaders()
    {
        bool hasChanges = false;
        foreach (var src in _sources)
        {
            var keysToMove = src.Headers.Keys.Where(k => SensitiveHeaders.Contains(k)).ToList();
            foreach (var k in keysToMove)
            {
                string rawVal = src.Headers[k];
                if (!string.IsNullOrEmpty(rawVal) && !rawVal.StartsWith("secret://"))
                {
                    string secretId = $"dyn_header_{src.Id}_{k.ToLowerInvariant()}";
                    try
                    {
                        SecretStorageService.SaveSecret(secretId, rawVal);
                        src.SecretHeaders[k] = secretId;
                        src.Headers.Remove(k);
                        hasChanges = true;
                    }
                    catch (Exception ex)
                    {
                        AppDataPath.LogError($"DynamicDataService.MigrateSensitiveHeaders failed for key {k}", ex);
                    }
                }
            }
        }

        if (hasChanges)
        {
            SaveSourcesInternal(_sources);
        }
    }

    public static OperationResult SaveSources(List<DynamicDataSource> sources)
    {
        try
        {
            foreach (var src in sources)
            {
                var keysToMove = src.Headers.Keys.Where(k => SensitiveHeaders.Contains(k)).ToList();
                foreach (var k in keysToMove)
                {
                    string rawVal = src.Headers[k];
                    if (!string.IsNullOrEmpty(rawVal) && !rawVal.StartsWith("secret://"))
                    {
                        string secretId = $"dyn_header_{src.Id}_{k.ToLowerInvariant()}";
                        SecretStorageService.SaveSecret(secretId, rawVal);
                        src.SecretHeaders[k] = secretId;
                        src.Headers.Remove(k);
                    }
                }
            }

            _sources = sources;
            return SaveSourcesInternal(_sources);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("DynamicDataService.SaveSources", ex);
            return OperationResult.Fail($"动态数据源保存失败: {ex.Message}", ex);
        }
    }

    private static OperationResult SaveSourcesInternal(List<DynamicDataSource> sources)
    {
        try
        {
            string json = JsonSerializer.Serialize(sources, new JsonSerializerOptions { WriteIndented = true });
            SafeFileWriter.WriteAllText(AppDataPath.DynamicDataFile, json);
            return OperationResult.Success("保存成功。");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"写入数据文件失败: {ex.Message}", ex);
        }
    }

    public static async Task<string> FetchValueAsync(DynamicDataSource source, bool forceRefresh = false)
    {
        if (!forceRefresh && source.LastCachedTime.HasValue && source.CacheTtlMinutes > 0)
        {
            if (DateTime.Now - source.LastCachedTime.Value < TimeSpan.FromMinutes(source.CacheTtlMinutes))
            {
                if (!string.IsNullOrEmpty(source.LastCachedValue))
                {
                    return source.LastCachedValue;
                }
            }
        }

        try
        {
            if (source.Type == DynamicDataSourceType.AiText)
            {
                return await FetchAiTextSourceAsync(source);
            }
            else
            {
                return await FetchHttpSourceAsync(source);
            }
        }
        catch (Exception ex)
        {
            source.LastError = ex.Message;
            source.LastErrorTime = DateTime.Now;
            AppDataPath.LogError($"DynamicDataService.FetchValueAsync ({source.Name})", ex);

            return !string.IsNullOrEmpty(source.LastCachedValue) ? source.LastCachedValue : "——";
        }
    }

    private static async Task<string> FetchHttpSourceAsync(DynamicDataSource source)
    {
        var testResult = await TestSourceAsync(source);
        if (!testResult.Success)
        {
            source.LastError = testResult.Error;
            source.LastErrorTime = DateTime.Now;
            return !string.IsNullOrEmpty(source.LastCachedValue) ? source.LastCachedValue : "——";
        }

        source.LastCachedValue = testResult.Value;
        source.LastCachedTime = DateTime.Now;
        source.LastSuccessfulTime = DateTime.Now;
        source.LastError = null;
        SaveSourcesInternal(_sources);
        return testResult.Value;
    }

    public static async Task<DynamicDataTestResult> TestSourceAsync(DynamicDataSource source)
    {
        try
        {
            bool hasSecrets = source.SecretHeaders.Count > 0;
            EndpointSecurityPolicy.ValidateEndpoint(source.Url, hasSecrets);
            using var request = CreateHttpRequest(source);
            using var response = HttpSenderOverride != null
                ? await HttpSenderOverride(request)
                : await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string rawContent = await response.Content.ReadAsStringAsync();
            var extraction = ExtractValue(rawContent, source.ExtractionType, source.ExtractionRule);
            if (!extraction.IsSuccess)
            {
                return new DynamicDataTestResult { RawContent = rawContent, Error = extraction.ErrorMessage };
            }

            return new DynamicDataTestResult
            {
                Success = true,
                RawContent = rawContent,
                Value = (source.FormatPrefix ?? "") + extraction.Value + (source.FormatSuffix ?? "")
            };
        }
        catch (Exception ex)
        {
            return new DynamicDataTestResult { Error = ex.Message };
        }
    }

    private static HttpRequestMessage CreateHttpRequest(DynamicDataSource source)
    {
        var request = new HttpRequestMessage(
            source.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get,
            source.Url);
        request.Headers.UserAgent.ParseAdd($"NewDesk/{AppVersionService.Version}");

        foreach (var pair in source.Headers)
        {
            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }
        foreach (var pair in source.SecretHeaders)
        {
            string? value = SecretStorageService.GetSecret(pair.Value);
            if (!string.IsNullOrEmpty(value))
            {
                request.Headers.TryAddWithoutValidation(pair.Key, value);
            }
        }

        return request;
    }

    private static async Task<string> FetchAiTextSourceAsync(DynamicDataSource source)
    {
        if (string.IsNullOrWhiteSpace(source.AiPrompt))
        {
            return source.LastCachedValue ?? "——";
        }

        AiProviderConfig? provider = null;
        if (!string.IsNullOrEmpty(source.AiProviderId))
        {
            provider = AiProviderRegistry.GetAllProviders().FirstOrDefault(p => p.ProviderId == source.AiProviderId);
        }

        provider ??= AiProviderRegistry.GetDefaultProvider();
        if (provider == null)
        {
            source.LastError = "No AI Provider available";
            source.LastErrorTime = DateTime.Now;
            return source.LastCachedValue ?? "——";
        }

        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(provider.BaseUrl);
        var settings = SettingsService.LoadSettings();

        if (!isLocal)
        {
            if (!source.AllowBackgroundCloudRequests || !settings.AllowBackgroundCloudRequests)
            {
                source.LastError = "后台云端 AI 请求未获持久授权";
                source.LastErrorTime = DateTime.Now;
                return source.LastCachedValue ?? "——";
            }
        }

        var req = new AiTurnRequest
        {
            UserPrompt = source.AiPrompt,
            TaskProfile = AiTaskProfile.WallpaperGeneration,
            PreferredProvider = provider,
            ModelOverride = source.AiModelId,
            CloudRequestMode = CloudRequestMode.PreAuthorizedBackground,
            DataCategories = AiDataCategory.DynamicData,
            DataSensitivity = DataSensitivity.Personal,
            EnableTools = false
        };

        var resp = await AiOrchestrator.ExecuteTurnAsync(req);
        string result = (source.FormatPrefix ?? "") + (resp.Content ?? "").Trim() + (source.FormatSuffix ?? "");

        if (!string.IsNullOrEmpty(resp.Content))
        {
            source.LastCachedValue = result;
            source.LastCachedTime = DateTime.Now;
            source.LastSuccessfulTime = DateTime.Now;
            source.LastError = null;
            SaveSourcesInternal(_sources);
            return result;
        }

        return source.LastCachedValue ?? "——";
    }

    private static ExtractionResult ExtractValue(string rawContent, string extractionType, string rule)
    {
        if (string.IsNullOrEmpty(rule) || extractionType == "Raw") return ExtractionResult.Success(rawContent);

        if (extractionType == "JsonPath" || extractionType == "JSONPath" || extractionType == "Json")
        {
            if (JsonPathExtractor.TryExtract(rawContent, rule, out string val))
            {
                return ExtractionResult.Success(val);
            }
            return ExtractionResult.Fail($"JsonPath extraction failed for rule '{rule}'.");
        }

        if (extractionType == "Regex")
        {
            try
            {
                var match = Regex.Match(rawContent, rule);
                if (match.Success)
                {
                    string res = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    return ExtractionResult.Success(res);
                }
            }
            catch (Exception ex)
            {
                return ExtractionResult.Fail($"Regex evaluation error: {ex.Message}");
            }
            return ExtractionResult.Fail($"Regex match failed for rule '{rule}'.");
        }

        return ExtractionResult.Success(rawContent);
    }

    public static List<DynamicDataSource> GetDefaultPresets()
    {
        return new List<DynamicDataSource>
        {
            new DynamicDataSource
            {
                Id = "preset_weather",
                Name = "北京实时天气",
                Type = DynamicDataSourceType.Http,
                Url = "https://wttr.in/Beijing?format=j1",
                ExtractionType = "Regex",
                ExtractionRule = "\"temp_C\":\\s*\"(\\d+)\"",
                FormatPrefix = "北京：",
                FormatSuffix = "°C",
                CacheTtlMinutes = 30
            },
            new DynamicDataSource
            {
                Id = "preset_crypto",
                Name = "Bitcoin 比特币价格",
                Type = DynamicDataSourceType.Http,
                Url = "https://api.coindesk.com/v1/bpi/currentprice.json",
                ExtractionType = "JsonPath",
                ExtractionRule = "$.bpi.USD.rate",
                FormatPrefix = "BTC: $",
                FormatSuffix = "",
                CacheTtlMinutes = 10
            }
        };
    }
}
