namespace S1Panel.Tray;

/// <summary>
/// A small append-only log (s1panel-tray.log next to the settings file) of starts, exits and errors, so an app
/// that quietly disappeared leaves a trace. Kept under ~256 KB by trimming the older half.
/// </summary>
internal static class TrayLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string settingsPath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
        _path = dir is null ? null : Path.Combine(dir, "s1panel-tray.log");
    }

    public static void Write(string message)
    {
        if (_path is null) return;
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > 256 * 1024)
                {
                    string all = File.ReadAllText(_path);
                    File.WriteAllText(_path, all[(all.Length / 2)..]);
                }
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // logging must never take the app down
            }
        }
    }

    public static void Error(string where, Exception ex) => Write($"ERROR in {where}: {ex}");
}
