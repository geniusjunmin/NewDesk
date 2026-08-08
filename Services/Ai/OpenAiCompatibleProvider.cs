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

public class OpenAiCompatibleProvider : IAiProvider
{
    private readonly HttpClient _httpClient;

    public string ProviderId => Config.ProviderId;
    public AiProviderConfig Config { get; }
    public AiProviderCapabilities Capabilities { get; }

    public OpenAiCompatibleProvider(AiProviderConfig config, HttpClient? httpClient = null)
    {
        Config = config;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds)) };

        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(config.BaseUrl);

        bool defaultSupportsTools = config.Kind switch
        {
            AiProviderKind.OpenAICompatible or AiProviderKind.Custom => false,
            _ => true
        };
        bool defaultSupportsVision = config.Kind == AiProviderKind.OpenAI;
        bool defaultSupportsStructuredOutput = config.Kind != AiProviderKind.OpenAICompatible && config.Kind != AiProviderKind.Custom;

        Capabilities = new AiProviderCapabilities
        {
            SupportsStreaming = config.Streaming,
            SupportsVision = config.SupportsVisionOverride ?? defaultSupportsVision,
            SupportsTools = config.SupportsToolsOverride ?? defaultSupportsTools,
            SupportsStructuredOutput = config.SupportsStructuredOutputOverride ?? defaultSupportsStructuredOutput,
            SupportsReasoning = config.Kind == AiProviderKind.DeepSeek,
            SupportsResponsesApi = false,
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
                Message = "✓ 连接成功",
                LatencyMs = sw.ElapsedMilliseconds,
                ModelCount = models.Count
            };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            int? statusCode = (int?)ex.StatusCode;
            string msg = statusCode switch
            {
                401 => "API Key 无效或没有权限 (HTTP 401)。",
                429 => "请求过多，请稍后再试 (HTTP 429)。",
                _ => $"无法连接 API: {ex.Message}"
            };
            return new AiConnectionResult
            {
                IsSuccess = false,
                Message = msg,
                LatencyMs = sw.ElapsedMilliseconds,
                HttpStatusCode = statusCode
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AiConnectionResult
            {
                IsSuccess = false,
                Message = $"连接失败: {ex.Message}",
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

        string url = Config.BaseUrl.TrimEnd('/') + "/chat/completions";
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
        string? reasoningSummary = null;
        var toolCalls = new List<AiToolCall>();

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var msgObj = choices[0].GetProperty("message");
            if (msgObj.TryGetProperty("content", out var cElem) && cElem.ValueKind == JsonValueKind.String)
            {
                content = cElem.GetString() ?? "";
            }
            if (msgObj.TryGetProperty("reasoning_summary", out var rsElem) && rsElem.ValueKind == JsonValueKind.String)
            {
                reasoningSummary = rsElem.GetString();
            }
            else if (msgObj.TryGetProperty("summary", out var sElem) && sElem.ValueKind == JsonValueKind.String)
            {
                reasoningSummary = sElem.GetString();
            }

            if (msgObj.TryGetProperty("tool_calls", out var tcElem) && tcElem.ValueKind == JsonValueKind.Array)
            {
                toolCalls = ParseToolCalls(tcElem);
            }
        }

        var usage = new AiUsage();
        if (root.TryGetProperty("usage", out var uElem))
        {
            if (uElem.TryGetProperty("prompt_tokens", out var pt)) usage.PromptTokens = pt.GetInt32();
            if (uElem.TryGetProperty("completion_tokens", out var ct)) usage.CompletionTokens = ct.GetInt32();
        }

        return new AiResponse
        {
            Content = content,
            ReasoningSummary = reasoningSummary,
            Usage = usage,
            ToolCalls = toolCalls
        };
    }

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateEndpointBeforeSend();

        string url = Config.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(req);

        var bodyObj = BuildRequestBody(request, stream: true);
        req.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var toolCallAccumulator = new Dictionary<int, (string id, string name, StringBuilder args)>();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                string data = line[6..].Trim();
                if (data == "[DONE]")
                {
                    if (toolCallAccumulator.Count > 0)
                    {
                        var toolCalls = toolCallAccumulator.Values.Select(v => new AiToolCall
                        {
                            Id = v.id,
                            Name = v.name,
                            ArgumentsJson = v.args.ToString()
                        }).ToList();

                        yield return new AiStreamChunk { ToolCalls = toolCalls, IsDone = true };
                    }
                    else
                    {
                        yield return new AiStreamChunk { IsDone = true };
                    }
                    yield break;
                }

                AiStreamChunk? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        string? textDelta = delta.TryGetProperty("content", out var cElem) ? cElem.GetString() : null;
                        
                        string? reasoningDelta = null;
                        if (delta.TryGetProperty("reasoning_summary", out var rsElem) && rsElem.ValueKind == JsonValueKind.String)
                        {
                            reasoningDelta = rsElem.GetString();
                        }
                        else if (delta.TryGetProperty("summary", out var sElem) && sElem.ValueKind == JsonValueKind.String)
                        {
                            reasoningDelta = sElem.GetString();
                        }

                        string? finishReason = choices[0].TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

                        if (delta.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tcItem in tcArr.EnumerateArray())
                            {
                                int index = tcItem.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                                string id = tcItem.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
                                string name = "";

                                if (tcItem.TryGetProperty("function", out var fnElem))
                                {
                                    if (fnElem.TryGetProperty("name", out var nElem)) name = nElem.GetString() ?? "";
                                    string argChunk = fnElem.TryGetProperty("arguments", out var aElem) ? aElem.GetString() ?? "" : "";

                                    if (!toolCallAccumulator.TryGetValue(index, out var existing))
                                    {
                                        existing = (id, name, new StringBuilder());
                                    }

                                    if (!string.IsNullOrEmpty(id)) existing.id = id;
                                    if (!string.IsNullOrEmpty(name)) existing.name = name;
                                    existing.args.Append(argChunk);

                                    toolCallAccumulator[index] = existing;
                                }
                            }
                        }

                        chunk = new AiStreamChunk
                        {
                            TextDelta = textDelta,
                            ReasoningDelta = reasoningDelta,
                            FinishReason = finishReason
                        };
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
        var messagesList = new List<object>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messagesList.Add(new { role = "system", content = request.SystemPrompt });
        }

        foreach (var msg in request.Messages)
        {
            if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                messagesList.Add(new
                {
                    role = "assistant",
                    content = msg.Content,
                    tool_calls = msg.ToolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        type = "function",
                        function = new { name = tc.Name, arguments = tc.ArgumentsJson }
                    }).ToList()
                });
            }
            else if (msg.Role == "tool")
            {
                messagesList.Add(new
                {
                    role = "tool",
                    tool_call_id = msg.ToolCallId ?? "",
                    content = msg.Content
                });
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
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = stream
        };

        if (Capabilities.SupportsTools && request.Tools != null && request.Tools.Count > 0)
        {
            reqBody["tools"] = ChatCompletionsToolMapper.MapTools(request.Tools);
            reqBody["tool_choice"] = "auto";
        }

        return reqBody;
    }

    private static List<AiToolCall> ParseToolCalls(JsonElement toolCallsArray)
    {
        var result = new List<AiToolCall>();
        foreach (var tc in toolCallsArray.EnumerateArray())
        {
            string id = tc.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "";
            if (tc.TryGetProperty("function", out var fnElem))
            {
                string name = fnElem.TryGetProperty("name", out var nElem) ? nElem.GetString() ?? "" : "";
                string args = fnElem.TryGetProperty("arguments", out var aElem) ? aElem.GetString() ?? "{}" : "{}";

                result.Add(new AiToolCall
                {
                    Id = id,
                    Name = name,
                    ArgumentsJson = args
                });
            }
        }
        return result;
    }
}
