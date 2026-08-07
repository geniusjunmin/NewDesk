using System.Threading.Tasks;
using NewDesk.Models.Ai;

namespace NewDesk.Services.Ai.Tools;

public interface IAiTool
{
    string Name { get; }
    string Description { get; }
    bool RequiresUserConfirmation { get; }
    object ParametersSchema { get; }

    Task<AiToolResult> ExecuteAsync(string argumentsJson);
}
