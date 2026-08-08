using System.Collections.Generic;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai.Protocol;

public static class AnthropicToolMapper
{
    public static List<object> MapTools(IEnumerable<AiToolDefinition> tools)
    {
        var result = new List<object>();
        foreach (var tool in tools)
        {
            result.Add(new
            {
                name = tool.Name,
                description = tool.Description,
                input_schema = tool.ParametersSchema ?? new { type = "object", properties = new { } }
            });
        }
        return result;
    }
}
