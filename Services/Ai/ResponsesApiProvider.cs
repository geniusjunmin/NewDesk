using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewDesk.Models.Ai;
using NewDesk.Services.Ai.Protocol;
using NewDesk.Services.Security;

namespace NewDesk.Services.Ai;

public class ResponsesApiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;

    public string ProviderId => Config.ProviderId;
    public AiProviderConfig Config { get; }
    public AiProviderCapabilities Capabilities { get; }

    public ResponsesApiProvider(AiProviderConfig config, HttpClient? httpClient = null)
    {
        Config = config;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds)) };

        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(config.BaseUrl);

        Capabilities = new AiProviderCapabilities
        {
            SupportsStreaming = config.Streaming,
            SupportsVision = config.SupportsVisionOverride ?? true,
            SupportsTools = config.SupportsToolsOverride ?? true,
            SupportsStructuredOutput = config.SupportsStructuredOutputOverride ?? true,
            SupportsReasoning = true,
            SupportsResponsesApi = true,
            SupportsModelListing = true,
            IsLocal = isLocal
        };
    }

    private void ValidateEndpointBeforeSend()
    {
        string? secret = SecretStorageService.GetSecret(Config.SecretId);
        bool transmitsSecrets = !string.IsNullOrEmpty(secret);
        AiHttpSecurityGuard.ValidateRequest(Config.BaseUrl, transmitsSecrets);
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        string? secret = SecretStorageService.GetSecret(Config.SecretId);
        if (!string.IsNullOrEmpty(secret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }
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
                Message = "✓ Responses API 连接成功",
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
                Message = $"Responses API 连接失败: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        ValidateEndpointBeforeSend();

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
                string id = elem.GetProperty("id").GetString() ?? "";
                if (!string.IsNullOrEmpty(id))
                {
                    list.Add(new AiModelInfo { Id = id, Name = id, OwnedBy = Config.Name });
                }
            }
        }
        return list;
    }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEndpointBeforeSend();

        string url = Config.BaseUrl.TrimEnd('/') + "/responses";
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

        // 1. Primary: Output items array parsing
        if (root.TryGetProperty("output", out var outputArr) && outputArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in outputArr.EnumerateArray())
            {
                string itemType = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (itemType == "message" && item.TryGetProperty("content", out var contentList) && contentList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in contentList.EnumerateArray())
                    {
                        if (c.TryGetProperty("text", out var tElem))
                        {
                            content += tElem.GetString();
                        }
                    }
                }
                else if (itemType == "function_call")
                {
                    string callId = item.TryGetProperty("call_id", out var cid) ? cid.GetString() ?? "" : "";
                    string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string args = item.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";

                    toolCalls.Add(new AiToolCall { Id = callId, Name = name, ArgumentsJson = args });
                }
            }
        }
        // 2. Secondary fallback: output_text
        if (string.IsNullOrEmpty(content) && root.TryGetProperty("output_text", out var outText))
        {
            content = outText.GetString() ?? "";
        }

        return new AiResponse
        {
            Content = content,
            Usage = new AiUsage(),
            ToolCalls = toolCalls
        };
    }

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateEndpointBeforeSend();

        string url = Config.BaseUrl.TrimEnd('/') + "/responses";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(req);

        var bodyObj = BuildRequestBody(request, stream: true);
        req.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Item ID -> (call_id, name) mapping
        var itemMap = new Dictionary<string, (string callId, string name)>();
        // Item ID -> arguments StringBuilder
        var argAccumulator = new Dictionary<string, StringBuilder>();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                string data = line[6..].Trim();
                if (data == "[DONE]")
                {
                    yield return new AiStreamChunk { IsDone = true };
                    yield break;
                }

                AiStreamChunk? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    string eventType = root.TryGetProperty("type", out var typeElem) ? typeElem.GetString() ?? "" : "";

                    switch (eventType)
                    {
                        case "response.output_item.added":
                        case "response.output_item.done":
                            if (root.TryGetProperty("item", out var itemElem))
                            {
                                string type = itemElem.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                                string itemId = itemElem.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                                if (type == "function_call" && !string.IsNullOrEmpty(itemId))
                                {
                                    string callId = itemElem.TryGetProperty("call_id", out var cid) ? cid.GetString() ?? itemId : itemId;
                                    string name = itemElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                    itemMap[itemId] = (callId, name);
                                }
                            }
                            break;

                        case "response.output_text.delta":
                            string deltaText = root.TryGetProperty("delta", out var dt) ? dt.GetString() ?? "" : "";
                            chunk = new AiStreamChunk { TextDelta = deltaText };
                            break;

                        case "response.function_call_arguments.delta":
                            string itemIdDelta = root.TryGetProperty("item_id", out var iid) ? iid.GetString() ?? "" : "";
                            string argChunk = root.TryGetProperty("delta", out var ad) ? ad.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(itemIdDelta))
                            {
                                if (!argAccumulator.TryGetValue(itemIdDelta, out var sb))
                                {
                                    sb = new StringBuilder();
                                    argAccumulator[itemIdDelta] = sb;
                                }
                                sb.Append(argChunk);
                            }
                            break;

                        case "response.function_call_arguments.done":
                            string doneItemId = root.TryGetProperty("item_id", out var iidDone) ? iidDone.GetString() ?? "" : "";
                            string doneArgs = root.TryGetProperty("arguments", out var argDone) ? argDone.GetString() ?? "" : "";

                            itemMap.TryGetValue(doneItemId, out var mapped);
                            string finalCallId = string.IsNullOrEmpty(mapped.callId) ? doneItemId : mapped.callId;
                            string finalName = mapped.name;

                            if (argAccumulator.TryGetValue(doneItemId, out var accumulatedSb) && accumulatedSb.Length > 0)
                            {
                                doneArgs = accumulatedSb.ToString();
                            }

                            chunk = new AiStreamChunk
                            {
                                ToolCalls = new List<AiToolCall>
                                {
                                    new AiToolCall { Id = finalCallId, Name = finalName, ArgumentsJson = doneArgs }
                                }
                            };
                            break;

                        case "response.completed":
                            chunk = new AiStreamChunk { IsDone = true };
                            break;

                        default:
                            if (root.TryGetProperty("delta", out var deltaObj))
                            {
                                string? text = deltaObj.TryGetProperty("text", out var tElem) ? tElem.GetString() : null;
                                chunk = new AiStreamChunk { TextDelta = text };
                            }
                            break;
                    }
                }
                catch { }

                if (chunk != null)
                {
                    yield return chunk;
                }
            }
        }
    }

    private object BuildRequestBody(AiRequest request, bool stream)
    {
        var input = new List<object>();

        foreach (var msg in request.Messages)
        {
            if (msg.Role == "tool")
            {
                input.Add(new
                {
                    type = "function_call_output",
                    call_id = msg.ToolCallId ?? "",
                    output = msg.Content
                });
            }
            else if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                foreach (var tc in msg.ToolCalls)
                {
                    input.Add(new
                    {
                        type = "function_call",
                        call_id = tc.Id,
                        name = tc.Name,
                        arguments = tc.ArgumentsJson
                    });
                }
            }
            else
            {
                input.Add(new { role = msg.Role, content = msg.Content });
            }
        }

        var reqBody = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrEmpty(request.Model) ? Config.SelectedModel : request.Model,
            ["input"] = input,
            ["stream"] = stream
        };

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            reqBody["instructions"] = request.SystemPrompt;
        }

        if (Capabilities.SupportsTools && request.Tools != null && request.Tools.Count > 0)
        {
            reqBody["tools"] = ResponsesToolMapper.MapTools(request.Tools);
        }

        return reqBody;
    }
}
