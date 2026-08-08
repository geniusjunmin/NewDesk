using System;
using System.Net;

namespace NewDesk.Services.Security;

public static class NetworkEndpointClassifier
{
    public static bool IsLocalEndpoint(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        string host = uri.Host.Trim().ToLowerInvariant();

        if (host == "localhost" || host == "127.0.0.1" || host == "::1")
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return IPAddress.IsLoopback(ipAddress);
        }

        return false;
    }
}
