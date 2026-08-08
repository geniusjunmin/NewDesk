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
        var dueAt = DateTime.Now.AddDays(1).ToString("O");
        var res = AiToolArgumentValidator.ValidateArguments("create_reminder", $"{{\"title\":\"交水费\",\"scheduleType\":\"OneTime\",\"dueAt\":\"{dueAt}\"}}");
        Assert.True(res.IsValid);
    }

    [Fact]
    public void ValidateArguments_CreateReminder_MissingTitle_ReturnsFail()
    {
        var res = AiToolArgumentValidator.ValidateArguments("create_reminder", "{\"month\":8}");
        Assert.False(res.IsValid);
        Assert.Contains("title", res.ErrorMessage);
    }

    [Fact]
    public void LunarYearly_Day30_ValidatorAllows()
    {
        var res = AiToolArgumentValidator.ValidateArguments(
            "create_reminder",
            "{\"title\":\"农历提醒\",\"scheduleType\":\"LunarYearly\",\"month\":2,\"day\":30,\"time\":\"09:00\"}");
        Assert.True(res.IsValid);
    }

    [Fact]
    public void UnknownScheduleType_Rejected()
    {
        var res = AiToolArgumentValidator.ValidateArguments(
            "create_reminder",
            "{\"title\":\"提醒\",\"scheduleType\":\"abc\",\"dueAt\":\"2099-01-01T09:00:00\"}");
        Assert.False(res.IsValid);
        Assert.Contains("scheduleType", res.ErrorMessage);
    }
}
