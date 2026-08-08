using System;
using System.Collections.Generic;

namespace NewDesk.Services;

public sealed class WallpaperRenderContext
{
    public IReadOnlyDictionary<string, string> DataSourceValues { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<Guid, string> LegacyApiValues { get; init; } =
        new Dictionary<Guid, string>();
}
