using System.IO;
using System.Text;

namespace CpuNetWidget;

internal static class AppDiagnostics
{
    private const long MaximumLogBytes = 1_048_576;
    private static readonly object Sync = new();

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CpuNetWidget", "Logs");

    public static string LogPath => Path.Combine(LogDirectory, "app.log");

    public static void Log(string context, Exception exception) =>
        Write($"{context}{Environment.NewLine}{exception}");

    public static void Log(string message) => Write(message);

    private static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                var entry = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Diagnostics must never interfere with monitoring or shutdown.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length < MaximumLogBytes) return;

        var previousPath = Path.Combine(LogDirectory, "app.previous.log");
        File.Move(LogPath, previousPath, overwrite: true);
    }
}
