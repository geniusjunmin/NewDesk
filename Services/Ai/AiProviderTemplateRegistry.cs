using System.Collections.Generic;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class AiProviderTemplateRegistry
{
    public static List<AiProviderConfig> GetTemplates()
    {
        return new List<AiProviderConfig>
        {
            new AiProviderConfig
            {
                ProviderId = "template_openai",
                Kind = AiProviderKind.OpenAI,
                Name = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                Protocol = AiApiProtocol.Responses,
                SelectedModel = "",
                IsDefault = false,
                IsEnabled = false
            },
            new AiProviderConfig
            {
                ProviderId = "template_gemini",
                Kind = AiProviderKind.Gemini,
                Name = "Google Gemini",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/",
                Protocol = AiApiProtocol.ChatCompletions,
                SelectedModel = "",
                IsDefault = false,
                IsEnabled = false
            },
            new AiProviderConfig
            {
                ProviderId = "template_xai",
                Kind = AiProviderKind.XAI,
                Name = "xAI (Grok)",
                BaseUrl = "https://api.x.ai/v1",
                Protocol = AiApiProtocol.Responses,
                SelectedModel = "",
                IsDefault = false,
                IsEnabled = false
            },
            new AiProviderConfig
            {
                ProviderId = "template_minimax",
                Kind = AiProviderKind.MiniMax,
                Name = "MiniMax",
                BaseUrl = "https://api.minimax.io/v1",
                Protocol = AiApiProtocol.ChatCompletions,
                SelectedModel = "",
                IsDefault = false,
                IsEnabled = false
            },
            new AiProviderConfig
            {
                ProviderId = "template_deepseek",
                Kind = AiProviderKind.DeepSeek,
                Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com",
                Protocol = AiApiProtocol.ChatCompletions,
                SelectedModel = "",
                IsDefault = false,
                IsEnabled = false
            },
            new AiProviderConfig
            {
                ProviderId = "template_claude",
                Kind = AiProviderKind.Claude,
                Name = "Anthropic Claude",
                BaseUrl = "https://api.anthropic.com/v1",
                Protocol = AiApiProtocol.AnthropicMessages,
                SelectedModel = "",
                IsDefault = false,
                IsEnabled = false
            },
            new AiProviderConfig
            {
                ProviderId = "template_ollama",
                Kind = AiProviderKind.Ollama,
                Name = "Ollama (Local)",
                BaseUrl = "http://localhost:11434/v1",
                Protocol = AiApiProtocol.ChatCompletions,
                SelectedModel = "",
                IsDefault = false,
                IsEnabled = true
            },
            new AiProviderConfig
            {
                ProviderId = "template_lmstudio",
                Kind = AiProviderKind.LMStudio,
                Name = "LM Studio (Local)",
                BaseUrl = "http://localhost:1234/v1",
                Protocol = AiApiProtocol.Auto,
                SelectedModel = "",
                IsDefault = false,
                IsEnabled = true
            }
        };
    }
}
