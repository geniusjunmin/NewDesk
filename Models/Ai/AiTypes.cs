using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NewDesk.Models.Ai;

public enum AiApiProtocol
{
    Auto,
    Responses,
    ChatCompletions,
    AnthropicMessages
}

public enum AiProviderKind
{
    OpenAI,
    Gemini,
    XAI,
    MiniMax,
    Claude,
    DeepSeek,
    Ollama,
    LMStudio,
    OpenAICompatible,
    Custom
}

public class AiProviderCapabilities
{
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsVision { get; set; } = false;
    public bool SupportsTools { get; set; } = true;
    public bool SupportsStructuredOutput { get; set; } = true;
    public bool SupportsReasoning { get; set; } = false;
    public bool SupportsResponsesApi { get; set; } = false;
    public bool SupportsModelListing { get; set; } = true;
    public bool IsLocal { get; set; } = false;

    // Overrides
    public bool? SupportsToolsOverride { get; set; }
    public bool? SupportsVisionOverride { get; set; }
    public bool? SupportsStructuredOutputOverride { get; set; }
}

public class AiProviderConfig
{
    public string ProviderId { get; set; } = Guid.NewGuid().ToString("N");
    public AiProviderKind Kind { get; set; } = AiProviderKind.OpenAI;
    public string Name { get; set; } = "OpenAI";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string? SecretId { get; set; }
    public string SelectedModel { get; set; } = "gpt-4o";
    public AiApiProtocol Protocol { get; set; } = AiApiProtocol.Auto;
    public bool Streaming { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 300;
    public bool IsEnabled { get; set; } = true;
    public bool IsDefault { get; set; } = false;

    public AiProviderConfig Clone()
    {
        return new AiProviderConfig
        {
            ProviderId = this.ProviderId,
            Kind = this.Kind,
            Name = this.Name,
            BaseUrl = this.BaseUrl,
            SecretId = this.SecretId,
            SelectedModel = this.SelectedModel,
            Protocol = this.Protocol,
            Streaming = this.Streaming,
            TimeoutSeconds = this.TimeoutSeconds,
            IsEnabled = this.IsEnabled,
            IsDefault = this.IsDefault
        };
    }
}

public class AiModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? OwnedBy { get; set; }
    public int? ContextWindow { get; set; }
}

public class AiConnectionResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public long LatencyMs { get; set; }
    public int ModelCount { get; set; }
    public int? HttpStatusCode { get; set; }
}

public class AiMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Role { get; set; } = "user"; // "system", "user", "assistant", "tool"
    public string Content { get; set; } = string.Empty;
    public string? ReasoningContent { get; set; }
    public string? ReasoningSummary { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public List<AiToolCall> ToolCalls { get; set; } = new();
    public string? ToolCallId { get; set; }
}

public class AiConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "新 AI 对话";
    public List<AiMessage> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
}

public class AiUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens => PromptTokens + CompletionTokens;
}

public class AiRequest
{
    public string Model { get; set; } = string.Empty;
    public List<AiMessage> Messages { get; set; } = new();
    public string? SystemPrompt { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public bool Stream { get; set; } = true;
    public List<AiToolDefinition> Tools { get; set; } = new();
}

public class AiResponse
{
    public string Content { get; set; } = string.Empty;
    public string? ReasoningSummary { get; set; }
    public AiUsage Usage { get; set; } = new();
    public List<AiToolCall> ToolCalls { get; set; } = new();
    public string? FinishReason { get; set; }
    public int ToolRounds { get; set; }
    public bool UsedTools => ToolRounds > 0;
}

public class AiStreamChunk
{
    public string? TextDelta { get; set; }
    public string? ReasoningDelta { get; set; }
    public bool IsDone { get; set; }
    public string? FinishReason { get; set; }
    public List<AiToolCall> ToolCalls { get; set; } = new();
}

public class AiToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object ParametersSchema { get; set; } = new { type = "object" };
}

public class AiToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
}

public class AiToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string OutputJson { get; set; } = "{}";
    public bool IsError { get; set; }
}
