using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NewDesk.Models;

namespace NewDesk.Services;

public static class DynamicDataService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static List<DynamicDataSource> _sources = new();

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
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("DynamicDataService.LoadSources", ex);
            _sources = GetDefaultPresets();
        }
        return _sources;
    }

    public static void SaveSources(List<DynamicDataSource> sources)
    {
        try
        {
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
            using var req = new HttpRequestMessage(source.Method.ToUpper() == "POST" ? HttpMethod.Post : HttpMethod.Get, source.Url);
            req.Headers.UserAgent.ParseAdd("NewDesk/2.0");

            foreach (var kvp in source.Headers)
            {
                req.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
            }

            using var resp = await HttpClient.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            string rawContent = await resp.Content.ReadAsStringAsync();
            string extractedValue = ExtractValue(rawContent, source.ExtractionType, source.ExtractionRule);

            string finalResult = (source.FormatPrefix ?? "") + extractedValue + (source.FormatSuffix ?? "");

            source.LastCachedValue = finalResult;
            source.LastCachedTime = DateTime.Now;
            SaveSources(_sources);

            return finalResult;
        }
        catch (Exception ex)
        {
            AppDataPath.LogError($"DynamicDataService.FetchValueAsync ({source.Name})", ex);
            return source.LastCachedValue ?? "(API Error)";
        }
    }

    private static string ExtractValue(string rawContent, string extractionType, string rule)
    {
        if (string.IsNullOrEmpty(rule)) return rawContent;

        if (extractionType == "JSONPath" || extractionType == "Json")
        {
            try
            {
                using var doc = JsonDocument.Parse(rawContent);
                var root = doc.RootElement;
                if (root.TryGetProperty(rule.TrimStart('.'), out var val))
                {
                    return val.ToString();
                }
            }
            catch { }
        }

        // Regex fallback
        try
        {
            var match = Regex.Match(rawContent, rule);
            if (match.Success)
            {
                return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
            }
        }
        catch { }

        return "(Regex Fail)";
    }

    public static List<DynamicDataSource> GetDefaultPresets()
    {
        return new List<DynamicDataSource>
        {
            new DynamicDataSource
            {
                Id = "preset_weather",
                Name = "北京实时天气",
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
                Url = "https://api.coindesk.com/v1/bpi/currentprice.json",
                ExtractionType = "Regex",
                ExtractionRule = "\"rate\":\\s*\"([^\"]+)\"",
                FormatPrefix = "BTC: $",
                FormatSuffix = "",
                CacheTtlMinutes = 10
            }
        };
    }
}
