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

            // Immediately run migration for legacy sensitive headers
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
                    SecretStorageService.SaveSecret(secretId, rawVal);
                    src.SecretHeaders[k] = secretId;
                    src.Headers.Remove(k);
                    hasChanges = true;
                }
            }
        }

        if (hasChanges)
        {
            SaveSources(_sources);
        }
    }

    public static void SaveSources(List<DynamicDataSource> sources)
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
            string json = JsonSerializer.Serialize(_sources, new JsonSerializerOptions { WriteIndented = true });
            SafeFileWriter.WriteAllText(AppDataPath.DynamicDataFile, json);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("DynamicDataService.SaveSources", ex);
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
        // HTTPS Enforcement check for non-loopback endpoints transmitting secrets
        bool hasSecrets = source.SecretHeaders.Count > 0;
        EndpointSecurityPolicy.ValidateEndpoint(source.Url, hasSecrets);

        using var req = new HttpRequestMessage(source.Method.ToUpper() == "POST" ? HttpMethod.Post : HttpMethod.Get, source.Url);
        req.Headers.UserAgent.ParseAdd("NewDesk/2.2");

        // Public Headers
        foreach (var kvp in source.Headers)
        {
            req.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        // Secret Headers from DPAPI
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
        SaveSources(_sources);

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

        // Background Cloud AI check
        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(provider.BaseUrl);
        var settings = SettingsService.LoadSettings();

        if (!isLocal && !settings.AllowBackgroundCloudRequests)
        {
            source.LastError = "Background cloud AI requests disabled";
            source.LastErrorTime = DateTime.Now;
            return source.LastCachedValue ?? "——";
        }

        var req = new AiTurnRequest
        {
            UserPrompt = source.AiPrompt,
            TaskProfile = AiTaskProfile.WallpaperGeneration,
            PreferredProvider = provider,
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
            SaveSources(_sources);
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
