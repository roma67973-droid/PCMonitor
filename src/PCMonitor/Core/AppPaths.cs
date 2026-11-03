using System.IO;

namespace PCMonitor.Core;

/// <summary>
/// Где программа хранит свои данные. Предпочтительно — рядом с exe (портативный режим);
/// если туда нельзя писать (например, Program Files) — в %LocalAppData%\PCMonitor.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string Settings => Path.Combine(DataDirectory, "settings.json");

    public static string History => Path.Combine(DataDirectory, "history.bin");

    private static string ResolveDataDirectory()
    {
        var portable = AppContext.BaseDirectory;
        if (IsWritable(portable))
        {
            return portable;
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCMonitor");
        Directory.CreateDirectory(local);
        return local;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
