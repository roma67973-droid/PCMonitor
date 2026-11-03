using System.Diagnostics;
using PCMonitor.Core;

namespace PCMonitor.Hardware;

/// <summary>
/// Фоновый цикл опроса датчиков. Работает в отдельном потоке с пониженным приоритетом;
/// пока окно скрыто, опрашивает только нужное для кулеров и трея и реже.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    private const int MinIntervalMs = 250;
    private const int MaxIntervalMs = 10_000;
    private const int BackgroundIntervalMs = 2_000;
    private const int IdleIntervalMs = 5_000;
    private const int ShutdownTimeoutMs = 4_000;

    private readonly AppSettings _settings;
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile bool _uiVisible;

    public HardwareMonitor(AppSettings settings) => _settings = settings;

    /// <summary>
    /// Вызывается из потока датчиков после каждого опроса. Обработчики должны быть быстрыми
    /// и сами переходить в UI-поток, если им это нужно.
    /// </summary>
    public event Action<Snapshot>? Updated;

    /// <summary>Окно открыто: опрашивать всё и с интервалом из настроек.</summary>
    public bool IsUiVisible
    {
        get => _uiVisible;
        set
        {
            _uiVisible = value;
            if (value)
            {
                Wake();
            }
        }
    }

    public void Start()
    {
        if (_thread is not null)
        {
            throw new InvalidOperationException("Мониторинг уже запущен.");
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Sensors", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Опросить датчики немедленно (например, после смены режима кулера).</summary>
    public void Wake() => _wake.Set();

    /// <summary>Останавливает опрос и возвращает кулеры под управление BIOS/драйвера.</summary>
    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        if (_thread is not null && !_thread.Join(ShutdownTimeoutMs))
        {
            Log.Warn("Поток датчиков не завершился вовремя");
        }

        _wake.Dispose();
    }

    private void Run()
    {
        SensorReader reader;
        try
        {
            reader = new SensorReader(_settings);
        }
        catch (Exception e)
        {
            // Без датчиков программа бесполезна, но окно должно открыться и объяснить ситуацию.
            Log.Error("Не удалось инициализировать датчики", e);
            return;
        }

        var fans = new FanController(_settings);
        try
        {
            while (!_stopping)
            {
                var started = Stopwatch.GetTimestamp();
                Tick(reader, fans);
                var elapsed = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _wake.WaitOne(Math.Max(50, CurrentInterval(fans) - elapsed));
            }
        }
        finally
        {
            fans.ReleaseAll(reader);
            reader.Dispose();
        }
    }

    private void Tick(SensorReader reader, FanController fans)
    {
        try
        {
            var snapshot = reader.Read(full: _uiVisible);
            fans.Apply(snapshot, reader);
            Updated?.Invoke(snapshot);
        }
        catch (Exception e)
        {
            // Цикл датчиков обязан пережить любой сбой драйвера или подписчика — иначе кулеры останутся без присмотра.
            Log.Error("Ошибка в цикле опроса датчиков", e);
        }
    }

    private int CurrentInterval(FanController fans)
    {
        if (_uiVisible)
        {
            return Math.Clamp(_settings.IntervalMs, MinIntervalMs, MaxIntervalMs);
        }

        return fans.HasManualControl || _settings.TrayTemp ? BackgroundIntervalMs : IdleIntervalMs;
    }
}
