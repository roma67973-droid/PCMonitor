using System.IO;
using PCMonitor.Hardware;

namespace PCMonitor.Core;

/// <summary>Одна минута истории: средние значения и пики за минуту.</summary>
public readonly record struct HistoryPoint(
    long Minute,
    float CpuTemp,
    float CpuTempMax,
    float GpuTemp,
    float GpuTempMax,
    float CpuLoad,
    float GpuLoad)
{
    public DateTime LocalTime => DateTime.UnixEpoch.AddMinutes(Minute).ToLocalTime();

    public bool HasCpuTemp => !float.IsNaN(CpuTemp);

    public bool HasGpuTemp => !float.IsNaN(GpuTemp);
}

/// <summary>
/// История температур и нагрузки с поминутным усреднением. Хранит последние 7 дней
/// (≈10 тыс. точек, ~300 КБ) в двоичном файле. Пишется из потока датчиков, читается из UI.
/// </summary>
public sealed class TemperatureHistory
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private const int FormatVersion = 1;
    private const int SaveEveryMinutes = 10;

    private readonly object _gate = new();
    private readonly List<HistoryPoint> _points = [];
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;

    private MinuteAccumulator _current;
    private long _lastSavedMinute;

    public TemperatureHistory(string path, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _current = new MinuteAccumulator(-1);
        Load();
        _lastSavedMinute = NowMinute();
    }

    private static long RetentionMinutes => (long)Retention.TotalMinutes;

    public void Add(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var minute = NowMinute();
        bool saveDue;
        lock (_gate)
        {
            if (minute != _current.Minute)
            {
                CloseCurrentMinute();
                _current = new MinuteAccumulator(minute);
            }

            _current.Add(snapshot);
            saveDue = minute - _lastSavedMinute >= SaveEveryMinutes;
            if (saveDue)
            {
                _lastSavedMinute = minute;
            }
        }

        if (saveDue)
        {
            Save();
        }
    }

    /// <summary>Точки за последние <paramref name="minutes"/> минут, включая текущую незаконченную минуту.</summary>
    public IReadOnlyList<HistoryPoint> GetRange(int minutes)
    {
        var from = NowMinute() - minutes;
        lock (_gate)
        {
            var start = _points.FindIndex(p => p.Minute > from);
            var result = start < 0 ? new List<HistoryPoint>() : _points.GetRange(start, _points.Count - start);
            if (_current.TryBuild(out var partial))
            {
                result.Add(partial);
            }

            return result;
        }
    }

    /// <summary>Сохранение при выходе: незаконченная минута тоже попадает в файл.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            CloseCurrentMinute();
            _current = new MinuteAccumulator(-1);
        }

        Save();
    }

    public void Save()
    {
        HistoryPoint[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _points];
        }

        try
        {
            var tmp = _path + ".tmp";
            using (var writer = new BinaryWriter(File.Create(tmp)))
            {
                writer.Write(FormatVersion);
                writer.Write(snapshot.Length);
                foreach (var p in snapshot)
                {
                    writer.Write(p.Minute);
                    writer.Write(p.CpuTemp);
                    writer.Write(p.CpuTempMax);
                    writer.Write(p.GpuTemp);
                    writer.Write(p.GpuTempMax);
                    writer.Write(p.CpuLoad);
                    writer.Write(p.GpuLoad);
                }
            }

            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Не удалось сохранить историю в {_path}", e);
        }
    }

    private long NowMinute() => _clock().ToUnixTimeSeconds() / 60;

    private void CloseCurrentMinute()
    {
        if (!_current.TryBuild(out var point))
        {
            return;
        }

        _points.Add(point);

        // Чистим пачками раз в час, а не на каждой минуте.
        var cutoff = point.Minute - RetentionMinutes;
        if (_points.Count > RetentionMinutes + 60 && _points[0].Minute <= cutoff)
        {
            _points.RemoveAll(p => p.Minute <= cutoff);
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            using var reader = new BinaryReader(File.OpenRead(_path));
            if (reader.ReadInt32() != FormatVersion)
            {
                Log.Warn($"Файл истории {_path} другой версии — начинаем с чистого листа");
                return;
            }

            var count = reader.ReadInt32();
            var cutoff = NowMinute() - RetentionMinutes;
            for (var i = 0; i < count; i++)
            {
                var point = new HistoryPoint(
                    reader.ReadInt64(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());
                if (point.Minute > cutoff)
                {
                    _points.Add(point);
                }
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or UnauthorizedAccessException)
        {
            Log.Warn($"Файл истории {_path} повреждён — начинаем с чистого листа", e);
            _points.Clear();
        }
    }

    /// <summary>Накопитель значений за одну минуту.</summary>
    private struct MinuteAccumulator(long minute)
    {
        private double _cpuTempSum;
        private double _gpuTempSum;
        private double _cpuLoadSum;
        private double _gpuLoadSum;
        private float _cpuTempMax = float.NaN;
        private float _gpuTempMax = float.NaN;
        private int _cpuTempCount;
        private int _gpuTempCount;
        private int _count;

        public readonly long Minute => minute;

        public void Add(Snapshot s)
        {
            if (s.CpuTemp is { } cpu)
            {
                _cpuTempSum += cpu;
                _cpuTempCount++;
                _cpuTempMax = float.IsNaN(_cpuTempMax) ? cpu : Math.Max(_cpuTempMax, cpu);
            }

            if (s.GpuTemp is { } gpu)
            {
                _gpuTempSum += gpu;
                _gpuTempCount++;
                _gpuTempMax = float.IsNaN(_gpuTempMax) ? gpu : Math.Max(_gpuTempMax, gpu);
            }

            _cpuLoadSum += s.CpuLoad;
            _gpuLoadSum += s.GpuLoad;
            _count++;
        }

        public readonly bool TryBuild(out HistoryPoint point)
        {
            if (_count == 0 || minute < 0)
            {
                point = default;
                return false;
            }

            point = new HistoryPoint(
                minute,
                _cpuTempCount > 0 ? (float)(_cpuTempSum / _cpuTempCount) : float.NaN,
                _cpuTempMax,
                _gpuTempCount > 0 ? (float)(_gpuTempSum / _gpuTempCount) : float.NaN,
                _gpuTempMax,
                (float)(_cpuLoadSum / _count),
                (float)(_gpuLoadSum / _count));
            return true;
        }
    }
}
