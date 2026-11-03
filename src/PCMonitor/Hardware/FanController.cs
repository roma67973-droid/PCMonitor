using LibreHardwareMonitor.Hardware;
using PCMonitor.Core;

namespace PCMonitor.Hardware;

/// <summary>
/// Применяет пользовательские режимы кулеров к железу.
/// Гарантирует, что кулер, отпущенный пользователем в «Авто» (или при выходе из программы),
/// возвращается под управление BIOS/драйвера.
/// </summary>
internal sealed class FanController
{
    /// <summary>Шаг изменения мощности за опрос: разгон быстрый, замедление плавное — кулер не «дёргается».</summary>
    private const double MaxStepUp = 12;
    private const double MaxStepDown = 3;

    /// <summary>Некоторые контроллеры сами сбрасывают ручной режим — периодически переотправляем значения.</summary>
    private const int ReapplyEveryTicks = 30;

    private readonly AppSettings _settings;
    private readonly Dictionary<string, float> _applied = [];
    private readonly HashSet<string> _overridden = [];
    private readonly HashSet<string> _released = [];
    private readonly HashSet<string> _failed = [];
    private long _tick;

    public FanController(AppSettings settings) => _settings = settings;

    /// <summary>Есть ли кулеры под ручным управлением (тогда опрос нельзя сильно замедлять).</summary>
    public bool HasManualControl => _overridden.Count > 0;

    public void Apply(Snapshot snapshot, SensorReader sensors)
    {
        _tick++;
        if (_tick % ReapplyEveryTicks == 0)
        {
            _applied.Clear();
        }

        var emergency = _settings.FailSafe
            && ((snapshot.CpuTemp ?? 0) >= Thermals.EmergencyCpu || (snapshot.GpuTemp ?? 0) >= Thermals.EmergencyGpu);

        foreach (var (key, control) in sensors.Controls)
        {
            _settings.Fans.TryGetValue(key, out var config);
            if (config is null || config.Mode == FanMode.Auto)
            {
                Release(key, control);
                continue;
            }

            var target = emergency ? 100 : ComputeTarget(key, config, sensors);
            if (target is { } percent)
            {
                SetSpeed(key, control, percent);
            }
        }
    }

    /// <summary>Вернуть все кулеры, которыми мы управляли, под управление BIOS/драйвера.</summary>
    public void ReleaseAll(SensorReader sensors)
    {
        foreach (var key in _overridden.ToList())
        {
            if (sensors.Controls.TryGetValue(key, out var control))
            {
                TrySetDefault(key, control);
            }
        }

        _overridden.Clear();
    }

    private double? ComputeTarget(string key, FanConfig config, SensorReader sensors)
    {
        if (config.Mode == FanMode.Fixed)
        {
            return config.Fixed;
        }

        if (config.Source is null || sensors.GetTemperature(config.Source) is not { } temperature)
        {
            // Датчик пропал — оставляем кулер как есть, а не гадаем.
            return null;
        }

        var target = FanCurve.Evaluate(config.Curve, temperature);
        return _applied.TryGetValue(key, out var previous)
            ? previous + Math.Clamp(target - previous, -MaxStepDown, MaxStepUp)
            : target;
    }

    private void SetSpeed(string key, IControl control, double percent)
    {
        float min = control.MinSoftwareValue, max = control.MaxSoftwareValue;
        if (max <= min)
        {
            (min, max) = (0, 100);
        }

        var value = (float)Math.Clamp(Math.Round(percent), min, max);
        var unchanged = _applied.TryGetValue(key, out var last) && Math.Abs(last - value) < 1 && _overridden.Contains(key);
        if (unchanged)
        {
            return;
        }

        try
        {
            control.SetSoftware(value);
            _applied[key] = value;
            _overridden.Add(key);
            _released.Remove(key);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            LogOnce(key, $"Не удалось задать скорость кулера {key}", e);
        }
    }

    /// <summary>
    /// Отпускает кулер в авто. Первый вызов для каждого ключа сбрасывает его безусловно —
    /// на случай, если прошлый запуск программы завершился аварийно и оставил ручное значение.
    /// </summary>
    private void Release(string key, IControl control)
    {
        var firstSeen = _released.Add(key);
        if (!firstSeen && !_overridden.Contains(key))
        {
            return;
        }

        TrySetDefault(key, control);
        _overridden.Remove(key);
        _applied.Remove(key);
    }

    private void TrySetDefault(string key, IControl control)
    {
        try
        {
            control.SetDefault();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            LogOnce(key, $"Не удалось вернуть кулер {key} в авто", e);
        }
    }

    private void LogOnce(string key, string message, Exception e)
    {
        if (_failed.Add(key))
        {
            Log.Warn(message, e);
        }
    }
}
