using System;
using System.Net.Http;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class AiProviderFactory
{
    public static IAiProvider CreateProvider(AiProviderConfig config, HttpClient? httpClient = null)
    {
        if (config.Protocol == AiApiProtocol.AnthropicMessages || config.Kind == AiProviderKind.Claude)
        {
            return new AnthropicMessagesProvider(config, httpClient);
        }

        if (config.Protocol == AiApiProtocol.Responses || (config.Protocol == AiApiProtocol.Auto && (config.Kind == AiProviderKind.OpenAI || config.Kind == AiProviderKind.XAI)))
        {
            return new ResponsesApiProvider(config, httpClient);
        }

        return new OpenAiCompatibleProvider(config, httpClient);
    }
}
