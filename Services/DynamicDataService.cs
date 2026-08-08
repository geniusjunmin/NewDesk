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

public class ExtractionResult
{
    public bool IsSuccess { get; set; }
    public string Value { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;

    public static ExtractionResult Success(string val) => new ExtractionResult { IsSuccess = true, Value = val };
    public static ExtractionResult Fail(string msg) => new ExtractionResult { IsSuccess = false, ErrorMessage = msg };
}

public static class DynamicDataService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
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
        bool hasSecrets = source.SecretHeaders.Count > 0;
        EndpointSecurityPolicy.ValidateEndpoint(source.Url, hasSecrets);

        using var req = new HttpRequestMessage(source.Method.ToUpper() == "POST" ? HttpMethod.Post : HttpMethod.Get, source.Url);
        req.Headers.UserAgent.ParseAdd("NewDesk/2.2");

        foreach (var kvp in source.Headers)
        {
            req.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        foreach (var kvp in source.SecretHeaders)
        {
            string? secretVal = SecretStorageService.GetSecret(kvp.Value);
            if (!string.IsNullOrEmpty(secretVal))
            {
                req.Headers.TryAddWithoutValidation(kvp.Key, secretVal);
            }
        }

        using var resp = await HttpClient.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        string rawContent = await resp.Content.ReadAsStringAsync();
        var extractRes = ExtractValue(rawContent, source.ExtractionType, source.ExtractionRule);

        if (!extractRes.IsSuccess)
        {
            source.LastError = extractRes.ErrorMessage;
            source.LastErrorTime = DateTime.Now;

            return !string.IsNullOrEmpty(source.LastCachedValue) ? source.LastCachedValue : "——";
        }

        string finalResult = (source.FormatPrefix ?? "") + extractRes.Value + (source.FormatSuffix ?? "");

        source.LastCachedValue = finalResult;
        source.LastCachedTime = DateTime.Now;
        source.LastSuccessfulTime = DateTime.Now;
        source.LastError = null;
        SaveSourcesInternal(_sources);

        return finalResult;
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
            DataSensitivity = DataSensitivity.Personal
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
