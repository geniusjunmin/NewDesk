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

        Capabilities = new AiProviderCapabilities
        {
            SupportsStreaming = config.Streaming,
            SupportsVision = true,
            SupportsTools = true,
            SupportsStructuredOutput = true,
            SupportsReasoning = true,
            SupportsResponsesApi = true,
            SupportsModelListing = true,
            IsLocal = false
        };
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
        List<AiToolCall>? toolCalls = null;

        if (root.TryGetProperty("output_text", out var outText))
        {
            content = outText.GetString() ?? "";
        }

        if (root.TryGetProperty("function_call", out var fcElem) && fcElem.ValueKind == JsonValueKind.Object)
        {
            string callId = fcElem.TryGetProperty("call_id", out var cid) ? cid.GetString() ?? "" : "";
            string name = fcElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string args = fcElem.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";

            toolCalls = new List<AiToolCall>
            {
                new AiToolCall { Id = callId, Name = name, ArgumentsJson = args }
            };
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
        string url = Config.BaseUrl.TrimEnd('/') + "/responses";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(req);

        var bodyObj = BuildRequestBody(request, stream: true);
        req.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

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

                    if (root.TryGetProperty("delta", out var delta))
                    {
                        string? text = delta.TryGetProperty("text", out var tElem) ? tElem.GetString() : null;
                        chunk = new AiStreamChunk { TextDelta = text };
                    }
                    else if (root.TryGetProperty("response", out var respObj) && respObj.TryGetProperty("output_text", out var outElem))
                    {
                        chunk = new AiStreamChunk { TextDelta = outElem.GetString() };
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
                var firstTool = msg.ToolCalls[0];
                input.Add(new
                {
                    type = "function_call",
                    call_id = firstTool.Id,
                    name = firstTool.Name,
                    arguments = firstTool.ArgumentsJson
                });
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

        if (request.Tools != null && request.Tools.Count > 0)
        {
            reqBody["tools"] = request.Tools;
        }

        return reqBody;
    }
}
