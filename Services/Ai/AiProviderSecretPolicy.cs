using System;
using NewDesk.Models.Ai;
using NewDesk.Services.Security;

namespace NewDesk.Services.Ai;

public static class AiProviderSecretPolicy
{
    public static void Apply(AiProviderConfig config, string? apiKey, bool clearExistingSecret)
    {
        if (clearExistingSecret)
        {
            if (!string.IsNullOrEmpty(config.SecretId)) SecretStorageService.DeleteSecret(config.SecretId);
            config.SecretId = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (string.IsNullOrEmpty(config.SecretId)) config.SecretId = null;
            return;
        }

        config.SecretId ??= "secret_" + Guid.NewGuid().ToString("N");
        SecretStorageService.SaveSecret(config.SecretId, apiKey);
    }
}
