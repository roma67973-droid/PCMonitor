using System.IO;

namespace PCMonitor.Core;

/// <summary>
/// Простой файловый лог (<c>pcmonitor.log</c> рядом с настройками).
/// Файл ротируется при превышении <see cref="MaxBytes"/>. Сам лог никогда не бросает исключений.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string directory) => _path = Path.Combine(directory, "pcmonitor.log");

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN ", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (_path is null)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (Gate)
        {
            try
            {
                var file = new FileInfo(_path);
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Лог не должен ронять приложение: если диск недоступен — просто теряем строку.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
