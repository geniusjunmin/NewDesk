using System;
using System.Collections.Generic;
using System.Linq;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class AiConversationService
{
    private static List<AiConversation> _conversations = new();
    private static bool _isLoaded = false;
    private const int MaxContextMessages = 10;

    public static List<AiConversation> GetConversations()
    {
        EnsureLoaded();
        return _conversations.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    public static AiConversation CreateNewConversation(string title = "新 AI 对话", string? providerId = null, string? modelId = null)
    {
        EnsureLoaded();
        var conv = new AiConversation
        {
            Id = Guid.NewGuid(),
            Title = title,
            ProviderId = providerId,
            ModelId = modelId,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        _conversations.Add(conv);
        Save();
        return conv;
    }

    public static void SaveConversation(AiConversation conversation)
    {
        EnsureLoaded();
        conversation.UpdatedAt = DateTime.Now;
        int idx = _conversations.FindIndex(c => c.Id == conversation.Id);
        if (idx >= 0) _conversations[idx] = conversation;
        else _conversations.Add(conversation);

        Save();
    }

    public static void DeleteConversation(Guid id)
    {
        EnsureLoaded();
        _conversations.RemoveAll(c => c.Id == id);
        Save();
    }

    public static void ClearAll()
    {
        EnsureLoaded();
        _conversations.Clear();
        AiConversationStore.ClearAll();
    }

    public static List<AiMessage> GetTruncatedContextMessages(List<AiMessage> allMessages)
    {
        if (allMessages.Count <= MaxContextMessages)
        {
            return new List<AiMessage>(allMessages);
        }

        // Keep system messages plus recent N messages
        var result = new List<AiMessage>();
        result.AddRange(allMessages.Where(m => m.Role == "system"));
        result.AddRange(allMessages.Where(m => m.Role != "system").TakeLast(MaxContextMessages));
        return result;
    }

    private static void EnsureLoaded()
    {
        if (_isLoaded) return;
        _conversations = AiConversationStore.LoadConversations();
        _isLoaded = true;
    }

    private static void Save()
    {
        AiConversationStore.SaveConversations(_conversations);
    }
}
