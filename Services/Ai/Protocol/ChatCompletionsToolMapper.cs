using System.Collections.Generic;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai.Protocol;

public static class ChatCompletionsToolMapper
{
    public static List<object> MapTools(IEnumerable<AiToolDefinition> tools)
    {
        var result = new List<object>();
        foreach (var tool in tools)
        {
            result.Add(new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.ParametersSchema ?? new { type = "object", properties = new { } }
                }
            });
        }
        return result;
    }
}
