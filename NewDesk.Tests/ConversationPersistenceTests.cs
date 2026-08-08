using System.IO;
using NewDesk.Models.Ai;
using NewDesk.Services;
using NewDesk.Services.Ai;
using Xunit;

namespace NewDesk.Tests;

public sealed class ConversationPersistenceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"NewDeskConversationTests_{Guid.NewGuid():N}");

    public ConversationPersistenceTests()
    {
        AppEnvironment.SetTestEnvironment(_testRoot);
        AiConversationService.ResetForTesting();
    }

    public void Dispose()
    {
        AiConversationService.ResetForTesting();
        AppEnvironment.ResetToNormalEnvironment();
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [Fact]
    public void ConversationRestoresProvider()
    {
        var conversation = AiConversationService.CreateNewConversation(providerId: "provider-1", modelId: "historic-model");
        AiConversationService.ResetForTesting();
        var restored = AiConversationService.GetConversations().Single(item => item.Id == conversation.Id);
        Assert.Equal("provider-1", restored.ProviderId);
    }

    [Fact]
    public void ConversationRestoresModelOverride()
    {
        var conversation = AiConversationService.CreateNewConversation(providerId: "provider-1", modelId: "historic-model");
        AiConversationService.ResetForTesting();
        var restored = AiConversationService.GetConversations().Single(item => item.Id == conversation.Id);
        Assert.Equal("historic-model", restored.ModelId);
    }

    [Fact]
    public void SelectingConversationDoesNotOverwriteModelId()
    {
        var conversation = new AiConversation { ProviderId = "provider-1", ModelId = "historic-model" };
        var provider = new AiProviderConfig { ProviderId = "provider-1", SelectedModel = "new-default", IsEnabled = true };

        var selection = AiConversationSelection.Resolve(conversation, [provider]);

        Assert.Equal("historic-model", selection.ModelOverride);
        Assert.Equal("historic-model", conversation.ModelId);
    }

    [Fact]
    public void DeletedProviderDisablesSend()
    {
        var conversation = new AiConversation { ProviderId = "deleted", ModelId = "model" };
        var selection = AiConversationSelection.Resolve(conversation, []);
        Assert.False(selection.CanSend);
        Assert.Contains("已删除", selection.StatusMessage);
    }

    [Fact]
    public void ToolCardsPersistAcrossReload()
    {
        var conversation = AiConversationService.CreateNewConversation();
        conversation.Messages.Add(new AiMessage
        {
            Role = "assistant",
            Content = "done",
            ToolEvents = [new ToolExecutionDisplayInfo { Icon = "🔔", Title = "已创建提醒", Detail = "会议" }]
        });
        AiConversationService.SaveConversation(conversation);
        AiConversationService.ResetForTesting();

        var restored = AiConversationService.GetConversations().Single(item => item.Id == conversation.Id);
        Assert.Equal("已创建提醒", restored.Messages.Single().ToolEvents.Single().Title);
    }

    [Fact]
    public void DefaultToolDisplay_DoesNotExposeRawJson()
    {
        const string rawArguments = "{\"apiKey\":\"top-secret\"}";

        var display = ToolExecutionDisplayInfo.Format("unknown_tool", rawArguments, isSuccess: true);

        Assert.Equal("工具执行完成", display.Detail);
        Assert.DoesNotContain("top-secret", display.Detail);
        Assert.DoesNotContain(rawArguments, display.Detail);
    }

    [Fact]
    public void InvalidToolJson_DoesNotExposeRawText()
    {
        const string rawArguments = "top-secret-not-json";

        var display = ToolExecutionDisplayInfo.Format("unknown_tool", rawArguments, isSuccess: false);

        Assert.Equal("工具执行失败", display.Detail);
        Assert.DoesNotContain(rawArguments, display.Detail);
    }
}
