using NewDesk.Services.Security;

namespace NewDesk.Services.Ai;

public static class AiSecretStorageService
{
    public static void SaveSecret(string providerId, string secretValue)
    {
        SecretStorageService.SaveSecret(providerId, secretValue);
    }

    public static string? GetSecret(string providerId)
    {
        return SecretStorageService.GetSecret(providerId);
    }

    public static void DeleteSecret(string providerId)
    {
        SecretStorageService.DeleteSecret(providerId);
    }
}
