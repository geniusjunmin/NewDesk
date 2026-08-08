using System;
using NewDesk.Models.Security;
using Xunit;

namespace NewDesk.Tests;

public class EndpointSecurityPolicyTests
{
    [Fact]
    public void ValidateEndpoint_AllowsLocalHttpWithSecrets()
    {
        // Local loopback endpoints can transmit secrets over HTTP
        EndpointSecurityPolicy.ValidateEndpoint("http://localhost:11434/v1", transmitsSecrets: true);
        EndpointSecurityPolicy.ValidateEndpoint("http://127.0.0.1:1234/v1", transmitsSecrets: true);
    }

    [Fact]
    public void ValidateEndpoint_AllowsRemoteHttpsWithSecrets()
    {
        // Remote HTTPS endpoints can transmit secrets safely
        EndpointSecurityPolicy.ValidateEndpoint("https://api.openai.com/v1", transmitsSecrets: true);
    }

    [Fact]
    public void ValidateEndpoint_ThrowsOnRemoteHttpWithSecrets()
    {
        // Remote HTTP endpoints MUST NOT transmit secrets
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EndpointSecurityPolicy.ValidateEndpoint("http://api.openai.com/v1", transmitsSecrets: true));

        Assert.Contains("HTTPS", ex.Message);
    }

    [Fact]
    public void ValidateEndpoint_AllowsRemoteHttpWithoutSecrets()
    {
        // Remote HTTP endpoints without secret transmission are permitted
        EndpointSecurityPolicy.ValidateEndpoint("http://wttr.in/Beijing?format=j1", transmitsSecrets: false);
    }
}
