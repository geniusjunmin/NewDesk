using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewDesk.Models.Ai;
using NewDesk.Services.Ai.Protocol;
using NewDesk.Services.Security;

namespace NewDesk.Services.Ai;

public class AnthropicMessagesProvider : IAiProvider
{
    private readonly HttpClient _httpClient;

    public string ProviderId => Config.ProviderId;
    public AiProviderConfig Config { get; }
    public AiProviderCapabilities Capabilities { get; }

    public AnthropicMessagesProvider(AiProviderConfig config, HttpClient? httpClient = null)
    {
        Config = config;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds)) };

        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(config.BaseUrl);

        Capabilities = new AiProviderCapabilities
        {
            SupportsStreaming = config.Streaming,
            SupportsVision = true,
            SupportsTools = true,
            SupportsStructuredOutput = true,
            SupportsReasoning = true,
            SupportsResponsesApi = false,
            SupportsModelListing = true,
            IsLocal = isLocal
        };
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        string? secret = SecretStorageService.GetSecret(Config.SecretId);
        if (!string.IsNullOrEmpty(secret))
        {
            request.Headers.Add("x-api-key", secret);
        }
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.UserAgent.ParseAdd("NewDesk/2.2");
    }

    public async Task<AiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var models = await GetModelsAsync(cancellationToken);
            sw.Stop();
            return new AiConnectionResult
            {
                IsSuccess = true,
                Message = "✓ Anthropic API 连接成功",
                LatencyMs = sw.ElapsedMilliseconds,
                ModelCount = models.Count
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AiConnectionResult
            {
                IsSuccess = false,
                Message = $"Anthropic API 连接失败: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string url = Config.BaseUrl.TrimEnd('/') + "/models";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(req);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var list = new List<AiModelInfo>();
            if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in dataArr.EnumerateArray())
                {
                    string id = elem.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
                    string name = elem.TryGetProperty("display_name", out var dnElem) ? dnElem.GetString() ?? id : id;
                    if (!string.IsNullOrEmpty(id))
                    {
                        list.Add(new AiModelInfo { Id = id, Name = name, OwnedBy = "Anthropic" });
                    }
                }
            }

            if (list.Count > 0) return list;
        }
        catch { }

        // Fallback static list
        return new List<AiModelInfo>
        {
            new AiModelInfo { Id = "claude-3-5-sonnet-20241022", Name = "Claude 3.5 Sonnet", OwnedBy = "Anthropic" },
            new AiModelInfo { Id = "claude-3-5-haiku-20241022", Name = "Claude 3.5 Haiku", OwnedBy = "Anthropic" },
            new AiModelInfo { Id = "claude-3-opus-20240229", Name = "Claude 3 Opus", OwnedBy = "Anthropic" }
        };
    }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        string url = Config.BaseUrl.TrimEnd('/') + "/messages";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(req);

        var bodyObj = BuildRequestBody(request, stream: false);
        req.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        string json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string content = "";
        var toolCalls = new List<AiToolCall>();

        if (root.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in contentArr.EnumerateArray())
            {
                string type = elem.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (type == "text" && elem.TryGetProperty("text", out var tElem))
                {
                    content += tElem.GetString();
                }
                else if (type == "tool_use")
                {
                    string id = elem.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
                    string name = elem.TryGetProperty("name", out var nElem) ? nElem.GetString() ?? "" : "";
                    string inputJson = elem.TryGetProperty("input", out var inElem) ? inElem.GetRawText() : "{}";

                    toolCalls.Add(new AiToolCall { Id = id, Name = name, ArgumentsJson = inputJson });
                }
            }
        }

        var usage = new AiUsage();
        if (root.TryGetProperty("usage", out var uElem))
        {
            if (uElem.TryGetProperty("input_tokens", out var it)) usage.PromptTokens = it.GetInt32();
            if (uElem.TryGetProperty("output_tokens", out var ot)) usage.CompletionTokens = ot.GetInt32();
        }

        return new AiResponse
        {
            Content = content,
            Usage = usage,
            ToolCalls = toolCalls
        };
    }

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string url = Config.BaseUrl.TrimEnd('/') + "/messages";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(req);

        var bodyObj = BuildRequestBody(request, stream: true);
        req.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string currentToolId = "";
        string currentToolName = "";
        var currentJsonSb = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                string data = line[6..].Trim();
                AiStreamChunk? chunk = null;
                bool isDone = false;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeElem))
                    {
                        string type = typeElem.GetString() ?? "";

                        if (type == "content_block_start" && root.TryGetProperty("content_block", out var cb))
                        {
                            string cbType = cb.TryGetProperty("type", out var cbt) ? cbt.GetString() ?? "" : "";
                            if (cbType == "tool_use")
                            {
                                currentToolId = cb.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                                currentToolName = cb.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                currentJsonSb.Clear();
                            }
                        }
                        else if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta))
                        {
                            string deltaType = delta.TryGetProperty("type", out var dt) ? dt.GetString() ?? "" : "";
                            if (deltaType == "text_delta" && delta.TryGetProperty("text", out var tElem))
                            {
                                chunk = new AiStreamChunk { TextDelta = tElem.GetString() };
                            }
                            else if (deltaType == "input_json_delta" && delta.TryGetProperty("partial_json", out var pj))
                            {
                                currentJsonSb.Append(pj.GetString());
                            }
                        }
                        else if (type == "content_block_stop" && !string.IsNullOrEmpty(currentToolId))
                        {
                            chunk = new AiStreamChunk
                            {
                                ToolCalls = new List<AiToolCall>
                                {
                                    new AiToolCall
                                    {
                                        Id = currentToolId,
                                        Name = currentToolName,
                                        ArgumentsJson = currentJsonSb.Length > 0 ? currentJsonSb.ToString() : "{}"
                                    }
                                }
                            };
                            currentToolId = "";
                            currentToolName = "";
                            currentJsonSb.Clear();
                        }
                        else if (type == "message_stop")
                        {
                            isDone = true;
                        }
                    }
                }
                catch { }

                if (isDone)
                {
                    yield return new AiStreamChunk { IsDone = true };
                    yield break;
                }

                if (chunk != null)
                {
                    yield return chunk;
                }
            }
        }
    }

    private object BuildRequestBody(AiRequest request, bool stream)
    {
        var messagesList = new List<object>();
        foreach (var msg in request.Messages)
        {
            if (msg.Role == "system") continue;

            if (msg.Role == "tool")
            {
                messagesList.Add(new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "tool_result", tool_use_id = msg.ToolCallId ?? "", content = msg.Content }
                    }
                });
            }
            else if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                var contentBlocks = new List<object>();
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    contentBlocks.Add(new { type = "text", text = msg.Content });
                }
                foreach (var tc in msg.ToolCalls)
                {
                    object inputObj = new { };
                    try { inputObj = JsonSerializer.Deserialize<object>(tc.ArgumentsJson) ?? new { }; } catch { }
                    contentBlocks.Add(new { type = "tool_use", id = tc.Id, name = tc.Name, input = inputObj });
                }
                messagesList.Add(new { role = "assistant", content = contentBlocks });
            }
            else
            {
                messagesList.Add(new { role = msg.Role, content = msg.Content });
            }
        }

        var reqBody = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrEmpty(request.Model) ? Config.SelectedModel : request.Model,
            ["messages"] = messagesList,
            ["max_tokens"] = request.MaxTokens > 0 ? request.MaxTokens : 2048,
            ["stream"] = stream
        };

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            reqBody["system"] = request.SystemPrompt;
        }

        if (Capabilities.SupportsTools && request.Tools != null && request.Tools.Count > 0)
        {
            reqBody["tools"] = AnthropicToolMapper.MapTools(request.Tools);
        }

        return reqBody;
    }
}
