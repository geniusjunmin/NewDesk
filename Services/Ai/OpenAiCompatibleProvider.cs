using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewDesk.Models.Ai;

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

        bool isLocal = config.Kind == AiProviderKind.Ollama || config.Kind == AiProviderKind.LMStudio || config.BaseUrl.Contains("localhost") || config.BaseUrl.Contains("127.0.0.1");

        Capabilities = new AiProviderCapabilities
        {
            SupportsStreaming = config.Streaming,
            SupportsVision = false,
            SupportsTools = true,
            SupportsStructuredOutput = true,
            SupportsReasoning = config.Kind == AiProviderKind.DeepSeek,
            SupportsResponsesApi = false,
            SupportsModelListing = true,
            IsLocal = isLocal
        };
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        string? secret = AiSecretStorageService.GetSecret(Config.SecretId);
        if (!string.IsNullOrEmpty(secret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }
        request.Headers.UserAgent.ParseAdd("NewDesk/2.0");
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
        string? reasoning = null;
        var toolCalls = new List<AiToolCall>();

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var msgObj = choices[0].GetProperty("message");
            if (msgObj.TryGetProperty("content", out var cElem) && cElem.ValueKind == JsonValueKind.String)
            {
                content = cElem.GetString() ?? "";
            }
            if (msgObj.TryGetProperty("reasoning_content", out var rElem) && rElem.ValueKind == JsonValueKind.String)
            {
                reasoning = rElem.GetString();
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
            ReasoningSummary = reasoning,
            Usage = usage,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null
        };
    }

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string url = Config.BaseUrl.TrimEnd('/') + "/chat/completions";
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

                    if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        string? textDelta = delta.TryGetProperty("content", out var cElem) ? cElem.GetString() : null;
                        string? reasoningDelta = delta.TryGetProperty("reasoning_content", out var rElem) ? rElem.GetString() : null;
                        string? finishReason = choices[0].TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

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
            messagesList.Add(new { role = msg.Role, content = msg.Content });
        }

        return new
        {
            model = string.IsNullOrEmpty(request.Model) ? Config.SelectedModel : request.Model,
            messages = messagesList,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            stream = stream
        };
    }
}
