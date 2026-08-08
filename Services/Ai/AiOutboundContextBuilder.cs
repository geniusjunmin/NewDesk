using System;
using System.Collections.Generic;
using System.Linq;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai;

public static class AiOutboundContextBuilder
{
    public static List<AiMessage> BuildMessages(IEnumerable<AiMessage> history, string currentUserPrompt)
    {
        var outbound = new List<AiMessage>();

        // 1. Redact history messages
        foreach (var msg in history)
        {
            var copy = new AiMessage
            {
                Id = msg.Id,
                Role = msg.Role,
                Content = SecretRedactor.Redact(msg.Content ?? string.Empty),
                ToolCallId = msg.ToolCallId,
                ToolCalls = msg.ToolCalls.Select(tc => new AiToolCall
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    ArgumentsJson = SecretRedactor.Redact(tc.ArgumentsJson)
                }).ToList()
            };
            outbound.Add(copy);
        }

        // 2. Redact and append current user prompt as the final turn
        string sanitizedPrompt = SecretRedactor.Redact(currentUserPrompt ?? string.Empty);
        outbound.Add(new AiMessage
        {
            Role = "user",
            Content = sanitizedPrompt
        });

        return outbound;
    }
}
