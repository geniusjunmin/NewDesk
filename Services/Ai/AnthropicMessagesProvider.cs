using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewDesk.Models.Ai;

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

        Capabilities = new AiProviderCapabilities
        {
            SupportsStreaming = config.Streaming,
            SupportsVision = true,
            SupportsTools = true,
            SupportsStructuredOutput = true,
            SupportsReasoning = true,
            SupportsResponsesApi = false,
            SupportsModelListing = false,
            IsLocal = false
        };
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        string? secret = AiSecretStorageService.GetSecret(Config.SecretId);
        if (!string.IsNullOrEmpty(secret))
        {
            request.Headers.Add("x-api-key", secret);
        }
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.UserAgent.ParseAdd("NewDesk/2.0");
    }

    public async Task<AiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var req = new AiRequest
            {
                Model = string.IsNullOrEmpty(Config.SelectedModel) ? "claude-3-5-sonnet-20241022" : Config.SelectedModel,
                Messages = new List<AiMessage> { new AiMessage { Role = "user", Content = "Hi" } },
                MaxTokens = 10,
                Stream = false
            };
            var resp = await CompleteAsync(req, cancellationToken);
            sw.Stop();
            return new AiConnectionResult
            {
                IsSuccess = true,
                Message = "✓ Anthropic API 连接成功",
                LatencyMs = sw.ElapsedMilliseconds,
                ModelCount = 1
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

    public Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<AiModelInfo>
        {
            new AiModelInfo { Id = "claude-3-5-sonnet-20241022", Name = "Claude 3.5 Sonnet", OwnedBy = "Anthropic" },
            new AiModelInfo { Id = "claude-3-5-haiku-20241022", Name = "Claude 3.5 Haiku", OwnedBy = "Anthropic" },
            new AiModelInfo { Id = "claude-3-opus-20240229", Name = "Claude 3 Opus", OwnedBy = "Anthropic" }
        };
        return Task.FromResult<IReadOnlyList<AiModelInfo>>(list);
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
        if (root.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in contentArr.EnumerateArray())
            {
                if (elem.TryGetProperty("text", out var tElem))
                {
                    content += tElem.GetString();
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
            Usage = usage
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
                        if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta))
                        {
                            if (delta.TryGetProperty("text", out var tElem))
                            {
                                chunk = new AiStreamChunk { TextDelta = tElem.GetString() };
                            }
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
            messagesList.Add(new { role = msg.Role, content = msg.Content });
        }

        return new
        {
            model = string.IsNullOrEmpty(request.Model) ? Config.SelectedModel : request.Model,
            messages = messagesList,
            system = request.SystemPrompt,
            max_tokens = request.MaxTokens,
            stream = stream
        };
    }
}
