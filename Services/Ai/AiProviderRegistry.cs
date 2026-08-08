using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewDesk.Models.Ai;
using NewDesk.Models.Security;
using NewDesk.Services.Security;

namespace NewDesk.Services.Ai;

public static class AiProviderRegistry
{
    private static List<AiProviderConfig> _providers = new();
    private static bool _isLoaded;

    public static IReadOnlyList<AiProviderConfig> GetAllProviders()
    {
        EnsureLoaded();
        return _providers.AsReadOnly();
    }

    public static AiProviderConfig? GetDefaultProvider()
    {
        EnsureLoaded();
        return _providers.FirstOrDefault(p => p.IsDefault && p.IsEnabled) ?? _providers.FirstOrDefault(p => p.IsEnabled);
    }

    public static void SaveProvider(AiProviderConfig config)
    {
        EnsureLoaded();

        // Security check
        EndpointSecurityPolicy.ValidateEndpoint(config.BaseUrl, transmitsSecrets: !string.IsNullOrEmpty(config.SecretId));

        int idx = _providers.FindIndex(p => p.ProviderId == config.ProviderId);
        if (idx >= 0)
        {
            _providers[idx] = config;
        }
        else
        {
            _providers.Add(config);
        }

        if (config.IsDefault)
        {
            foreach (var p in _providers)
            {
                if (p.ProviderId != config.ProviderId) p.IsDefault = false;
            }
        }

        PersistProviders();
    }

    public static void DeleteProvider(string providerId)
    {
        EnsureLoaded();
        var p = _providers.FirstOrDefault(x => x.ProviderId == providerId);
        if (p != null)
        {
            if (!string.IsNullOrEmpty(p.SecretId))
            {
                SecretStorageService.DeleteSecret(p.SecretId);
            }
            _providers.Remove(p);
            PersistProviders();
        }
    }

    private static void EnsureLoaded()
    {
        if (_isLoaded) return;
        _isLoaded = true;

        try
        {
            string path = AppDataPath.AiProvidersFile;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<List<AiProviderConfig>>(json) ?? new List<AiProviderConfig>();
                _providers = loaded;
            }
            else
            {
                _providers = new List<AiProviderConfig>();
            }
        }
        catch
        {
            _providers = new List<AiProviderConfig>();
        }
    }

    internal static void ResetForTesting()
    {
        _providers = new List<AiProviderConfig>();
        _isLoaded = false;
    }

    private static void PersistProviders()
    {
        try
        {
            string json = JsonSerializer.Serialize(_providers, new JsonSerializerOptions { WriteIndented = true });
            SafeFileWriter.WriteAllText(AppDataPath.AiProvidersFile, json);
        }
        catch (Exception ex)
        {
            AppDataPath.LogError("AiProviderRegistry.PersistProviders", ex);
            throw new IOException($"AI 服务配置保存失败: {ex.Message}", ex);
        }
    }
}
