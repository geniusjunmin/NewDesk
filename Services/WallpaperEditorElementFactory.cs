using NewDesk.Models;

namespace NewDesk.Services;

public static class WallpaperEditorElementFactory
{
    public static TextElementState? CreateDataSourceElement(DynamicDataSource? selectedSource)
    {
        if (selectedSource == null) return null;
        return new TextElementState
        {
            Text = $"{{{selectedSource.Name}}}",
            DynamicType = "DataSource",
            DataSourceId = selectedSource.Id,
            X = 100,
            Y = 280,
            FontSize = 48,
            Color = "#10B981"
        };
    }

    public static bool TrySetPosition(TextElementState element, double x, double y)
    {
        if (element.IsLocked) return false;
        element.X = System.Math.Max(0, x);
        element.Y = System.Math.Max(0, y);
        return true;
    }
}
