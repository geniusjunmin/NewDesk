using System.Collections.Generic;
using System.Linq;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public sealed class AiConversationSelection
{
    public AiProviderConfig? Provider { get; init; }
    public string? ModelOverride { get; init; }
    public bool CanSend => Provider != null;
    public string StatusMessage { get; init; } = string.Empty;

    public static AiConversationSelection Resolve(AiConversation conversation, IEnumerable<AiProviderConfig> providers)
    {
        var provider = providers.FirstOrDefault(item => item.ProviderId == conversation.ProviderId && item.IsEnabled);
        return provider == null
            ? new AiConversationSelection
            {
                ModelOverride = conversation.ModelId,
                StatusMessage = "原 AI 服务已删除，请选择新的 AI 服务。"
            }
            : new AiConversationSelection
            {
                Provider = provider,
                ModelOverride = string.IsNullOrEmpty(conversation.ModelId) ? provider.SelectedModel : conversation.ModelId
            };
    }
}
