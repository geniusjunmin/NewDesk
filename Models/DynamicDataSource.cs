using System;
using System.Collections.Generic;

namespace NewDesk.Models;

public enum DynamicDataSourceType
{
    Http,
    AiText
}

public class DynamicDataSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DynamicDataSourceType Type { get; set; } = DynamicDataSourceType.Http;

    // HTTP Source Config
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, string> SecretHeaders { get; set; } = new();

    [Obsolete("Legacy field. Use SecretHeaders dictionary instead.")]
    public string? SecretHeaderId { get; set; }

    public string ExtractionType { get; set; } = "JsonPath"; // "JsonPath", "Regex", or "Raw"
    public string ExtractionRule { get; set; } = string.Empty;
    public string FormatPrefix { get; set; } = string.Empty;
    public string FormatSuffix { get; set; } = string.Empty;

    // AI Text Source Config
    public string? AiProviderId { get; set; }
    public string? AiModelId { get; set; }
    public string? AiPrompt { get; set; }
    public bool AllowBackgroundCloudRequests { get; set; } = false;

    // Refresh & Cache Config
    public int RefreshIntervalMinutes { get; set; } = 0; // 0 = Manual
    public int CacheTtlMinutes { get; set; } = 15;
    public string? LastCachedValue { get; set; }
    public DateTime? LastCachedTime { get; set; }
    public DateTime? LastSuccessfulTime { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorTime { get; set; }
}
