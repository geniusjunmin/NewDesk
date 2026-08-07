using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class LocalModelScannerService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<List<AiProviderConfig>> ScanLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        var detectedList = new List<AiProviderConfig>();

        // 1. Scan Ollama (Port 11434)
        try
        {
            var ollamaConfig = new AiProviderConfig
            {
                ProviderId = "local_ollama",
                Kind = AiProviderKind.Ollama,
                Name = "Ollama (Local)",
                BaseUrl = "http://localhost:11434/v1",
                Protocol = AiApiProtocol.ChatCompletions,
                IsEnabled = true
            };
            var provider = AiProviderFactory.CreateProvider(ollamaConfig, HttpClient);
            var models = await provider.GetModelsAsync(cancellationToken);
            if (models.Count > 0)
            {
                ollamaConfig.SelectedModel = models[0].Id;
                detectedList.Add(ollamaConfig);
            }
        }
        catch { }

        // 2. Scan LM Studio (Port 1234)
        try
        {
            var lmStudioConfig = new AiProviderConfig
            {
                ProviderId = "local_lmstudio",
                Kind = AiProviderKind.LMStudio,
                Name = "LM Studio (Local)",
                BaseUrl = "http://localhost:1234/v1",
                Protocol = AiApiProtocol.Auto,
                IsEnabled = true
            };
            var provider = AiProviderFactory.CreateProvider(lmStudioConfig, HttpClient);
            var models = await provider.GetModelsAsync(cancellationToken);
            if (models.Count > 0)
            {
                lmStudioConfig.SelectedModel = models[0].Id;
                detectedList.Add(lmStudioConfig);
            }
        }
        catch { }

        return detectedList;
    }
}
