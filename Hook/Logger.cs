namespace ProtonNL.Hook;

internal static class Logger
{
    private static readonly string Path =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProtonNL.log");

    private static readonly object Gate = new();

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [hook] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path, line);
            }
            catch
            {
                // ignored
            }
        }
    }
}
