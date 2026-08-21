using System.Reflection;

namespace ProtonNL.Hook;

internal static class Runtime
{
    private static readonly object Gate = new();

    public static object? ConnectionManager { get; private set; }
    public static object? ServersLoader { get; private set; }

    public static string? ForcedCountry { get; set; }
    public static string? ForcedCity { get; set; }

    public static void CaptureManager(object? manager)
    {
        if (manager == null)
            return;

        lock (Gate)
        {
            ConnectionManager = manager;
            object? loader = GetInstanceField(manager, "_serversLoader");
            if (loader != null)
                ServersLoader = loader;
        }
    }

    public static void CaptureFromModerator(object? moderator)
    {
        if (moderator == null)
            return;

        CaptureManager(GetInstanceField(moderator, "_connectionManager"));
    }

    public static object? GetInstanceField(object instance, string name)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
                return field.GetValue(instance);
        }

        return null;
    }
}
