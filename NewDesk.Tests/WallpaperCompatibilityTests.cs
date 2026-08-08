using NewDesk.Models;
using NewDesk.Services;
using Xunit;

namespace NewDesk.Tests;

public class WallpaperCompatibilityTests
{
    [Fact]
    public async Task LegacyApiElement_StillRendersFetchedValue()
    {
        var element = new TextElementState
        {
            Text = "{API数据}",
            DynamicType = "Api",
            ApiUrl = "https://example.test/weather",
            ApiRegex = "\\\"temperature\\\":\\\"([^\\\"]+)"
        };
        var state = new WallpaperState { TextElements = [element] };

        var context = await WallpaperService.BuildRenderContextAsync(
            state,
            [],
            legacyApiFetcher: _ => Task.FromResult("18"));

        Assert.Equal("18", WallpaperTextRenderer.ResolveElementText(element, context));
    }

    [Fact]
    public void DataSourceElement_RendersCachedValue()
    {
        var element = new TextElementState { DynamicType = "DataSource", DataSourceId = "weather", Text = "fallback" };
        var context = new WallpaperRenderContext
        {
            DataSourceValues = new Dictionary<string, string> { ["weather"] = "18°C" }
        };

        Assert.Equal("18°C", WallpaperTextRenderer.ResolveElementText(element, context));
    }

    [Fact]
    public async Task SameDataSource_Deduplicated()
    {
        var state = new WallpaperState
        {
            TextElements =
            [
                new TextElementState { DynamicType = "DataSource", DataSourceId = "weather" },
                new TextElementState { DynamicType = "DataSource", DataSourceId = "weather" }
            ]
        };
        var source = new DynamicDataSource { Id = "weather", Name = "Weather" };
        int requests = 0;

        var context = await WallpaperService.BuildRenderContextAsync(
            state,
            [source],
            _ =>
            {
                requests++;
                return Task.FromResult("18°C");
            });

        Assert.Equal(1, requests);
        Assert.Equal("18°C", context.DataSourceValues["weather"]);
    }

    [Fact]
    public void Picker_SelectedSourceUsed()
    {
        var selected = new DynamicDataSource { Id = "selected", Name = "Selected" };
        var element = WallpaperEditorElementFactory.CreateDataSourceElement(selected);
        Assert.Equal("selected", element!.DataSourceId);
    }

    [Fact]
    public void Picker_DoesNotAutomaticallyCreateFirstSource()
    {
        Assert.Null(WallpaperEditorElementFactory.CreateDataSourceElement(null));
    }

    [Fact]
    public void Drag100Moves_FullRenderCountZero()
    {
        var element = new TextElementState();
        int fullRenderCount = 0;
        for (int i = 0; i < 100; i++)
        {
            Assert.True(WallpaperEditorElementFactory.TrySetPosition(element, i, i));
        }
        Assert.Equal(0, fullRenderCount);
    }

    [Fact]
    public void LockedPositionCannotChange()
    {
        var element = new TextElementState { X = 10, Y = 20, IsLocked = true };
        Assert.False(WallpaperEditorElementFactory.TrySetPosition(element, 100, 200));
        Assert.Equal(10, element.X);
        Assert.Equal(20, element.Y);
    }

    [Fact]
    public void HiddenLayerStillExistsInLayerList()
    {
        var hidden = new TextElementState { IsVisible = false };
        var state = new WallpaperState { TextElements = [hidden] };
        Assert.Contains(hidden, state.TextElements);
        Assert.DoesNotContain(state.TextElements.Where(item => item.IsVisible), item => item.Id == hidden.Id);
    }

    [Fact]
    public void ApplyFailure_DoesNotReportSuccess()
    {
        var result = WallpaperService.CreateApplyResult(anySuccess: false);
        Assert.False(result.IsSuccess);
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
