using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NewDesk.Services;

public static class JsonPathExtractor
{
    public static bool TryExtract(string json, string path, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement;

            string cleanPath = path.Trim();
            if (cleanPath.StartsWith("$.")) cleanPath = cleanPath[2..];
            else if (cleanPath.StartsWith("$")) cleanPath = cleanPath[1..];

            string[] segments = cleanPath.Split('.', StringSplitOptions.RemoveEmptyEntries);

            foreach (var seg in segments)
            {
                string propertyName = seg;
                int arrayIndex = -1;

                var match = Regex.Match(seg, @"^(.+)\[(\d+)\]$");
                if (match.Success)
                {
                    propertyName = match.Groups[1].Value;
                    arrayIndex = int.Parse(match.Groups[2].Value);
                }

                if (!string.IsNullOrEmpty(propertyName))
                {
                    if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(propertyName, out var nextProp))
                    {
                        current = nextProp;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (arrayIndex >= 0)
                {
                    if (current.ValueKind == JsonValueKind.Array && arrayIndex < current.GetArrayLength())
                    {
                        current = current[arrayIndex];
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            value = current.ValueKind switch
            {
                JsonValueKind.String => current.GetString() ?? string.Empty,
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => current.GetRawText()
            };

            return true;
        }
        catch
        {
            return false;
        }
    }
}
