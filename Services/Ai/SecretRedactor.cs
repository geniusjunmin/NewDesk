using System;
using System.Text.RegularExpressions;

namespace NewDesk.Services.Ai;

public static class SecretRedactor
{
    private static readonly Regex BearerRegex = new(@"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SkRegex = new(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled);
    private static readonly Regex KeyValueRegex = new(@"(api_?key|password|token|secret)\s*[:=]\s*[""']?([^""'\s;,]+)[""']?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        string redacted = input;
        redacted = BearerRegex.Replace(redacted, "Bearer [REDACTED]");
        redacted = SkRegex.Replace(redacted, "sk-••••••••••••••••");
        redacted = KeyValueRegex.Replace(redacted, "$1=[REDACTED]");

        return redacted;
    }
}
