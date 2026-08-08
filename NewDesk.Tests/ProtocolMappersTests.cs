using System.Collections.Generic;
using System.Text.Json;
using NewDesk.Models.Ai;
using NewDesk.Services.Ai.Protocol;
using Xunit;

namespace NewDesk.Tests;

public class ProtocolMappersTests
{
    [Fact]
    public void ChatCompletionsToolMapper_MapsToTypeFunctionStructure()
    {
        var tools = new List<AiToolDefinition>
        {
            new AiToolDefinition
            {
                Name = "test_func",
                Description = "A test tool",
                ParametersSchema = new { type = "object" }
            }
        };

        var mapped = ChatCompletionsToolMapper.MapTools(tools);
        Assert.Single(mapped);

        string json = JsonSerializer.Serialize(mapped[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("function", root.GetProperty("type").GetString());
        var func = root.GetProperty("function");
        Assert.Equal("test_func", func.GetProperty("name").GetString());
        Assert.Equal("A test tool", func.GetProperty("description").GetString());
        Assert.True(func.TryGetProperty("parameters", out _));
    }

    [Fact]
    public void ResponsesToolMapper_MapsToFlatFunctionStructure()
    {
        var tools = new List<AiToolDefinition>
        {
            new AiToolDefinition
            {
                Name = "resp_func",
                Description = "Responses tool",
                ParametersSchema = new { type = "object" }
            }
        };

        var mapped = ResponsesToolMapper.MapTools(tools);
        Assert.Single(mapped);

        string json = JsonSerializer.Serialize(mapped[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("function", root.GetProperty("type").GetString());
        Assert.Equal("resp_func", root.GetProperty("name").GetString());
        Assert.Equal("Responses tool", root.GetProperty("description").GetString());
    }

    [Fact]
    public void AnthropicToolMapper_MapsToInputSchemaStructure()
    {
        var tools = new List<AiToolDefinition>
        {
            new AiToolDefinition
            {
                Name = "claude_func",
                Description = "Claude tool",
                ParametersSchema = new { type = "object" }
            }
        };

        var mapped = AnthropicToolMapper.MapTools(tools);
        Assert.Single(mapped);

        string json = JsonSerializer.Serialize(mapped[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("claude_func", root.GetProperty("name").GetString());
        Assert.Equal("Claude tool", root.GetProperty("description").GetString());
        Assert.True(root.TryGetProperty("input_schema", out _));
    }
}
