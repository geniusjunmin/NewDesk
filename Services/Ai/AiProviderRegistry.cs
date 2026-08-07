using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class AiProviderRegistry
{
    private static List<AiProviderConfig> _providers = new();

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
                AiSecretStorageService.DeleteSecret(p.SecretId);
            }
            _providers.Remove(p);
            PersistProviders();
        }
    }

    private static void EnsureLoaded()
    {
        if (_providers.Count > 0) return;

        try
        {
            string path = AppDataPath.AiProvidersFile;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                _providers = JsonSerializer.Deserialize<List<AiProviderConfig>>(json) ?? GetDefaultPresets();
            }
            else
            {
                _providers = GetDefaultPresets();
                PersistProviders();
            }
        }
        catch
        {
            _providers = GetDefaultPresets();
        }
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
        }
    }

    public static List<AiProviderConfig> GetDefaultPresets()
    {
        return new List<AiProviderConfig>
        {
            new AiProviderConfig
            {
                ProviderId = "preset_openai",
                Kind = AiProviderKind.OpenAI,
                Name = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                Protocol = AiApiProtocol.Responses,
                SelectedModel = "gpt-4o",
                IsDefault = true,
                IsEnabled = true
            },
            new AiProviderConfig
            {
                ProviderId = "preset_gemini",
                Kind = AiProviderKind.Gemini,
                Name = "Google Gemini",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/",
                Protocol = AiApiProtocol.ChatCompletions,
                SelectedModel = "gemini-2.0-flash",
                IsEnabled = true
            },
            new AiProviderConfig
            {
                ProviderId = "preset_xai",
                Kind = AiProviderKind.XAI,
                Name = "xAI (Grok)",
                BaseUrl = "https://api.x.ai/v1",
                Protocol = AiApiProtocol.Responses,
                SelectedModel = "grok-2-latest",
                IsEnabled = true
            },
            new AiProviderConfig
            {
                ProviderId = "preset_minimax",
                Kind = AiProviderKind.MiniMax,
                Name = "MiniMax",
                BaseUrl = "https://api.minimax.io/v1",
                Protocol = AiApiProtocol.ChatCompletions,
                SelectedModel = "abab6.5s-chat",
                IsEnabled = true
            },
            new AiProviderConfig
            {
                ProviderId = "preset_deepseek",
                Kind = AiProviderKind.DeepSeek,
                Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com",
                Protocol = AiApiProtocol.ChatCompletions,
                SelectedModel = "deepseek-chat",
                IsEnabled = true
            },
            new AiProviderConfig
            {
                ProviderId = "preset_claude",
                Kind = AiProviderKind.Claude,
                Name = "Anthropic Claude",
                BaseUrl = "https://api.anthropic.com/v1",
                Protocol = AiApiProtocol.AnthropicMessages,
                SelectedModel = "claude-3-5-sonnet-20241022",
                IsEnabled = true
            },
            new AiProviderConfig
            {
                ProviderId = "preset_ollama",
                Kind = AiProviderKind.Ollama,
                Name = "Ollama (Local)",
                BaseUrl = "http://localhost:11434/v1",
                Protocol = AiApiProtocol.ChatCompletions,
                SelectedModel = "llama3:latest",
                IsEnabled = true
            },
            new AiProviderConfig
            {
                ProviderId = "preset_lmstudio",
                Kind = AiProviderKind.LMStudio,
                Name = "LM Studio (Local)",
                BaseUrl = "http://localhost:1234/v1",
                Protocol = AiApiProtocol.Auto,
                SelectedModel = "local-model",
                IsEnabled = true
            }
        };
    }
}
