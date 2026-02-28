namespace TranslatorTray.Services;

internal static class DebugLog
{
    private static readonly object Sync = new();
    private static readonly string LogPath;

    static DebugLog()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TranslatorTray");
        Directory.CreateDirectory(dir);
        LogPath = Path.Combine(dir, "debug.log");
    }

    public static string PathValue => LogPath;

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Best-effort diagnostics only.
        }
    }
}
