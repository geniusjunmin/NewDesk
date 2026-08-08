using System.Collections.Generic;
using System.Linq;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai.Tools;

public static class AiToolRegistry
{
    private static readonly Dictionary<string, IAiTool> ToolsMap = new()
    {
        { "create_reminder", new ReminderCreateTool() },
        { "switch_wallpaper", new WallpaperSwitchTool() },
        { "get_system_info", new SystemInfoTool() }
    };

    public static IReadOnlyCollection<IAiTool> GetAllTools()
    {
        return ToolsMap.Values.ToList().AsReadOnly();
    }

    public static IAiTool? GetTool(string name)
    {
        return ToolsMap.TryGetValue(name, out var tool) ? tool : null;
    }

    public static List<AiToolDefinition> GetToolDefinitions()
    {
        return GetToolDefinitions(ToolsMap.Keys);
    }

    public static List<AiToolDefinition> GetToolDefinitions(IEnumerable<string> allowedToolNames)
    {
        var allowed = new HashSet<string>(allowedToolNames, System.StringComparer.OrdinalIgnoreCase);
        var list = new List<AiToolDefinition>();
        foreach (var t in ToolsMap.Values.Where(t => allowed.Contains(t.Name)))
        {
            list.Add(new AiToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                ParametersSchema = t.ParametersSchema
            });
        }
        return list;
    }
}
