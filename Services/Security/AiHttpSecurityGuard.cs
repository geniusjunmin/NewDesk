using System;
using NewDesk.Models.Security;

namespace NewDesk.Services.Security;

public static class AiHttpSecurityGuard
{
    public static void ValidateRequest(string url, bool transmitsSecrets)
    {
        EndpointSecurityPolicy.ValidateEndpoint(url, transmitsSecrets);
    }
}
