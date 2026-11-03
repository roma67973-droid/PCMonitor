using System.Windows.Threading;
using PCMonitor.Platform;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>
/// Один физический монитор на вкладке «Экран». Слайдер меняет значение сразу,
/// а в монитор оно уходит, когда слайдер замер на <see cref="WriteDelay"/>: DDC/CI медленный.
/// </summary>
public sealed class MonitorViewModel : ObservableObject
{
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(250);

    private readonly DdcMonitor _monitor;
    private readonly Func<Func<bool>, Task<bool>> _runDdc;
    private readonly DispatcherTimer _brightnessTimer;
    private readonly DispatcherTimer _contrastTimer;
    private int _brightness;
    private int _contrast;
    private bool _isSupported;
    private bool _hasContrast;
    private string _status = string.Empty;

    /// <summary>Растёт при каждом изменении пользователем — по нему отбрасываются устаревшие чтения.</summary>
    private int _editVersion;

    /// <param name="runDdc">Выполняет команду DDC/CI в фоне, строго по одной на все мониторы.</param>
    public MonitorViewModel(DdcMonitor monitor, Func<Func<bool>, Task<bool>> runDdc)
    {
        _monitor = monitor;
        _runDdc = runDdc;
        _brightnessTimer = CreateWriteTimer(() => WriteAsync(_monitor.TrySetBrightness, _brightness));
        _contrastTimer = CreateWriteTimer(() => WriteAsync(_monitor.TrySetContrast, _contrast));
    }

    public string Name => _monitor.Name;

    public bool IsSupported
    {
        get => _isSupported;
        private set => SetProperty(ref _isSupported, value);
    }

    public bool HasContrast
    {
        get => _hasContrast;
        private set => SetProperty(ref _hasContrast, value);
    }

    /// <summary>Пусто, пока всё хорошо; иначе — что пошло не так.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public int Brightness
    {
        get => _brightness;
        set => Edit(ref _brightness, value, _brightnessTimer);
    }

    public int Contrast
    {
        get => _contrast;
        set => Edit(ref _contrast, value, _contrastTimer);
    }

    /// <summary>Прочитать текущие значения из монитора (их могли поменять кнопками на корпусе).</summary>
    /// <param name="probe">
    /// Первое чтение после поиска решает, управляется ли монитор вообще. Повторные чтения при сбое
    /// ничего не меняют: этот монитор изредка не отвечает и на все попытки подряд.
    /// </param>
    public async Task ReadAsync(bool probe = false)
    {
        var version = _editVersion;
        int brightness = 0, contrast = 0;
        var hasBrightness = await _runDdc(() => _monitor.TryGetBrightness(out brightness)).ConfigureAwait(true);
        var hasContrast = hasBrightness && await _runDdc(() => _monitor.TryGetContrast(out contrast)).ConfigureAwait(true);

        if (probe)
        {
            IsSupported = hasBrightness;
            HasContrast = hasContrast;
            Status = hasBrightness ? string.Empty : "Монитор не отвечает по DDC/CI. Включите DDC/CI в меню монитора или пользуйтесь затемнением.";
        }

        // Пока шло чтение, пользователь мог двинуть слайдер — его значение важнее.
        if (version != _editVersion || _brightnessTimer.IsEnabled || _contrastTimer.IsEnabled)
        {
            return;
        }

        if (hasBrightness)
        {
            SetProperty(ref _brightness, brightness, nameof(Brightness));
        }

        if (hasContrast)
        {
            SetProperty(ref _contrast, contrast, nameof(Contrast));
        }
    }

    /// <summary>
    /// Поставить яркость и отправить её в монитор, даже если на экране уже это число:
    /// монитор могли переключить кнопками, пока окно было закрыто.
    /// </summary>
    public void ApplyBrightness(int percent)
    {
        _brightness = Math.Clamp(percent, 0, 100);
        OnPropertyChanged(nameof(Brightness));
        _editVersion++;
        _brightnessTimer.Stop();
        _brightnessTimer.Start();
    }

    /// <summary>Сразу отправить отложенные записи (перед тем как модель заменят новой).</summary>
    public async Task FlushAsync()
    {
        if (_brightnessTimer.IsEnabled)
        {
            _brightnessTimer.Stop();
            await WriteAsync(_monitor.TrySetBrightness, _brightness).ConfigureAwait(true);
        }

        if (_contrastTimer.IsEnabled)
        {
            _contrastTimer.Stop();
            await WriteAsync(_monitor.TrySetContrast, _contrast).ConfigureAwait(true);
        }
    }

    public void StopPendingWrites()
    {
        _brightnessTimer.Stop();
        _contrastTimer.Stop();
    }

    private DispatcherTimer CreateWriteTimer(Func<Task> write)
    {
        var timer = new DispatcherTimer { Interval = WriteDelay };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await write().ConfigureAwait(true);
        };
        return timer;
    }

    private void Edit(ref int field, int value, DispatcherTimer timer, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, Math.Clamp(value, 0, 100), propertyName))
        {
            _editVersion++;
            timer.Stop();
            timer.Start();
        }
    }

    private async Task WriteAsync(Func<int, bool> set, int value)
    {
        var ok = await _runDdc(() => set(value)).ConfigureAwait(true);
        Status = ok ? string.Empty : "Монитор не принял команду — попробуйте ещё раз.";
    }
}
