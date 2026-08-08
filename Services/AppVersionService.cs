using System.Reflection;

namespace NewDesk.Services;

public static class AppVersionService
{
    public static string Version => "2.2.4";

    public static string DisplayVersion => $"NewDesk v{Version}";
}
