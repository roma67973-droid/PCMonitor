using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCMonitor.Core;

/// <summary>
/// Настройки пользователя. Сохраняются в JSON с задержкой (<see cref="SaveSoon"/>),
/// чтобы движение слайдера не превращалось в десятки записей на диск.
/// </summary>
public sealed class AppSettings : IDisposable
{
    private const int SaveDelayMs = 800;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _saveGate = new();
    private Timer? _saveTimer;
    private string _path = AppPaths.Settings;

    public string Theme { get; set; } = "Полночь";

    public bool StartMinimized { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public bool TrayTemp { get; set; } = true;

    public bool FailSafe { get; set; } = true;

    public bool HideEmptyFans { get; set; } = true;

    public bool Animations { get; set; } = true;

    public int IntervalMs { get; set; } = 1000;

    public double WindowWidth { get; set; } = 1220;

    public double WindowHeight { get; set; } = 800;

    /// <summary>Затемнение экрана поверх изображения, 0–100. Яркость подсветки не хранится — её помнит сам монитор.</summary>
    public int DimPercent { get; set; }

    /// <summary>Тёплый оттенок через гамму видеокарты, 0–100.</summary>
    public int WarmthPercent { get; set; }

    /// <summary>Настройки кулеров по идентификатору датчика управления. Читается из потока датчиков.</summary>
    public ConcurrentDictionary<string, FanConfig> Fans { get; set; } = new();

    /// <summary>Пользовательские имена кулеров.</summary>
    public ConcurrentDictionary<string, string> FanNames { get; set; } = new();

    [JsonIgnore]
    public string FilePath => _path;

    public static AppSettings Load(string path)
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось прочитать настройки {path}, используются значения по умолчанию", e);
        }

        settings ??= new AppSettings();
        settings._path = path;
        return settings;
    }

    public void SaveSoon()
    {
        lock (_saveGate)
        {
            _saveTimer ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(SaveDelayMs, Timeout.Infinite);
        }
    }

    /// <summary>Атомарная запись: сначала во временный файл, затем замена.</summary>
    public void SaveNow()
    {
        lock (_saveGate)
        {
            try
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Не удалось сохранить настройки в {_path}", e);
            }
        }
    }

    public void Dispose()
    {
        lock (_saveGate)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
    }
}
