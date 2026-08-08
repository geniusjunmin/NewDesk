using NewDesk.Services.Ai.Tools;
using Xunit;

namespace NewDesk.Tests;

public class AiToolValidatorTests
{
    [Fact]
    public void ValidateArguments_SwitchWallpaper_EmptyJsonObject_ReturnsSuccess()
    {
        var res = AiToolArgumentValidator.ValidateArguments("switch_wallpaper", "{}");
        Assert.True(res.IsValid);
    }

    [Fact]
    public void ValidateArguments_CreateReminder_ValidArguments_ReturnsSuccess()
    {
        var res = AiToolArgumentValidator.ValidateArguments("create_reminder", "{\"title\":\"交水费\",\"dueAt\":\"2026-08-09T15:00:00\"}");
        Assert.True(res.IsValid);
    }

    [Fact]
    public void ValidateArguments_CreateReminder_MissingTitle_ReturnsFail()
    {
        var res = AiToolArgumentValidator.ValidateArguments("create_reminder", "{\"month\":8}");
        Assert.False(res.IsValid);
        Assert.Contains("title", res.ErrorMessage);
    }
}
