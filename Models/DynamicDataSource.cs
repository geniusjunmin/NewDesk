using System;
using System.Collections.Generic;

namespace NewDesk.Models;

public class DynamicDataSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? SecretHeaderId { get; set; }
    public string ExtractionType { get; set; } = "Regex"; // "Regex" or "JSONPath"
    public string ExtractionRule { get; set; } = string.Empty;
    public string FormatPrefix { get; set; } = string.Empty;
    public string FormatSuffix { get; set; } = string.Empty;
    public int CacheTtlMinutes { get; set; } = 5;
    public string? LastCachedValue { get; set; }
    public DateTime? LastCachedTime { get; set; }
}
