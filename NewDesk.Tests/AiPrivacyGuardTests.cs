using System;
using NewDesk.Models.Ai;
using NewDesk.Services.Ai;
using Xunit;

namespace NewDesk.Tests;

public class AiPrivacyGuardTests
{
    [Fact]
    public void ValidateRequest_BlocksSecretsInPrompt()
    {
        var provider = new AiProviderConfig { BaseUrl = "http://localhost:11434/v1" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            AiPrivacyGuard.ValidateRequest(provider, DataSensitivity.Secret, AiDataCategory.UserPrompt, "MasterPassword input"));

        Assert.Contains("阻断", ex.Message);
    }

    [Fact]
    public void ValidateRequest_AllowsPublicUserPromptOnLocal()
    {
        var provider = new AiProviderConfig { BaseUrl = "http://localhost:11434/v1" };
        AiPrivacyGuard.ValidateRequest(provider, DataSensitivity.Public, AiDataCategory.UserPrompt, "Hello World");
    }
}
