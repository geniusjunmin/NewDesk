using System;
using NewDesk.Services.Security;

namespace NewDesk.Models.Security;

public static class EndpointSecurityPolicy
{
    public static void ValidateEndpoint(string url, bool transmitsSecrets)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("API Endpoint 地址不能为空。");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"API Endpoint 地址 '{url}' 格式不正确。");
        }

        bool isLocal = NetworkEndpointClassifier.IsLocalEndpoint(url);

        if (!isLocal && transmitsSecrets && uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"为了保护您的 API Key 与机密 Header，远程 Endpoint ({uri.Host}) 必须使用安全的 HTTPS 协议。禁用不安全的 HTTP 明文传输。");
        }
    }
}
