using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PCMonitor.Core;
using PCMonitor.Platform;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>
/// Вкладка «Экран»: яркость и контраст мониторов по DDC/CI, затемнение и тёплый оттенок.
/// Живёт всё время работы программы (фильтры должны действовать и при закрытом окне).
/// </summary>
public sealed class DisplayViewModel : ObservableObject, IDisposable
{
    /// <summary>Сколько ждать незаконченную команду DDC/CI при выходе, прежде чем закрыть мониторы.</summary>
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    /// <summary>Подсветка, затемнение и тепло каждого быстрого режима.</summary>
    private static readonly Dictionary<DisplayPreset, (int Brightness, int Dim, int Warmth)> Presets = new()
    {
        [DisplayPreset.Night] = (20, 30, 60),
        [DisplayPreset.Evening] = (50, 0, 35),
        [DisplayPreset.Day] = (80, 0, 0),
        [DisplayPreset.Max] = (100, 0, 0),
    };

    private readonly AppSettings _settings;
    private readonly ScreenFilters _filters;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SemaphoreSlim _ddcGate = new(1, 1);
    private List<DdcMonitor> _devices = [];
    private bool _isScanning;
    private bool _rescanRequested;
    private int? _pendingPresetBrightness;
    private DisplayPreset? _activePreset;
    private DisplayPreset? _lastAppliedPreset;
    private bool _warmthRejected;
    private volatile bool _disposed;

    public DisplayViewModel(AppSettings settings)
    {
        _settings = settings;
        // Файл настроек могли поправить руками.
        settings.DimPercent = Math.Clamp(settings.DimPercent, 0, 100);
        settings.WarmthPercent = Math.Clamp(settings.WarmthPercent, 0, 100);

        _filters = new ScreenFilters();
        _filters.SetDim(settings.DimPercent);
        _warmthRejected = !_filters.SetWarmth(settings.WarmthPercent);

        PresetCommand = new RelayCommand<DisplayPreset>(ApplyPreset);
        PresetOptions =
        [
            new(DisplayPreset.Night, "Ночь", DescribePreset(DisplayPreset.Night), this),
            new(DisplayPreset.Evening, "Вечер", DescribePreset(DisplayPreset.Evening), this),
            new(DisplayPreset.Day, "День", DescribePreset(DisplayPreset.Day), this),
            new(DisplayPreset.Max, "Максимум", DescribePreset(DisplayPreset.Max), this),
        ];
        RescanCommand = new RelayCommand(() => _ = RescanAsync());

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        Monitors.CollectionChanged += OnMonitorsChanged;
        _ = RescanAsync();
    }

    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    public ICommand PresetCommand { get; }

    public ICommand RescanCommand { get; }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(HasNoMonitors));
                if (!value)
                {
                    OnActivePresetChanged();
                }
            }
        }
    }

    public bool HasNoMonitors => !IsScanning && Monitors.Count == 0;

    /// <summary>
    /// Режим, которому соответствуют текущие значения, или <c>null</c>, если их подвигали вручную.
    /// Запись применяет режим — так к свойству привязываются кнопки-переключатели.
    /// </summary>
    public DisplayPreset? ActivePreset
    {
        get => _activePreset;
        set
        {
            if (value is { } preset)
            {
                ApplyPreset(preset);
            }
        }
    }

    /// <summary>Кнопки режимов в том порядке, в каком они на экране.</summary>
    public IReadOnlyList<PresetOptionViewModel> PresetOptions { get; }

    /// <summary>Подпись под кнопкой режима: что именно он ставит.</summary>
    private static string DescribePreset(DisplayPreset preset)
    {
        var (brightness, dim, warmth) = Presets[preset];
        var text = $"яркость {brightness}%";
        if (dim > 0)
        {
            text += $" · темнее {dim}%";
        }

        if (warmth > 0)
        {
            text += $" · тепло {warmth}%";
        }

        return text;
    }

    public int DimPercent
    {
        get => _settings.DimPercent;
        set
        {
            var v = Math.Clamp(value, 0, 100);
            if (_disposed || v == _settings.DimPercent)
            {
                return;
            }

            _settings.DimPercent = v;
            _filters.SetDim(v);
            _settings.SaveSoon();
            OnPropertyChanged();
            OnActivePresetChanged();
        }
    }

    public int WarmthPercent
    {
        get => _settings.WarmthPercent;
        set
        {
            var v = Math.Clamp(value, 0, 100);
            if (_disposed || v == _settings.WarmthPercent)
            {
                return;
            }

            _settings.WarmthPercent = v;
            WarmthRejected = !_filters.SetWarmth(v);
            _settings.SaveSoon();
            OnPropertyChanged();
            OnActivePresetChanged();
        }
    }

    /// <summary>Видеокарта или драйвер не дали поменять гамму (бывает с HDR и некоторыми драйверами).</summary>
    public bool WarmthRejected
    {
        get => _warmthRejected;
        private set => SetProperty(ref _warmthRejected, value);
    }

    /// <summary>Быстрые режимы: подсветка, затемнение, тепло.</summary>
    public void ApplyPreset(DisplayPreset preset)
    {
        if (_disposed)
        {
            return;
        }

        var (brightness, dim, warmth) = Presets[preset];

        if (IsScanning)
        {
            // Мониторы сейчас заменяются новыми — яркость поставим, когда поиск закончится.
            _pendingPresetBrightness = brightness;
        }
        else
        {
            ApplyBrightness(brightness);
        }

        DimPercent = dim;
        WarmthPercent = warmth;

        // Значения могли уже совпадать — кнопки всё равно должны показать выбранный режим.
        _lastAppliedPreset = preset;
        OnActivePresetChanged();
    }

    /// <summary>Вкладка открыта — перечитать значения: их могли поменять кнопками на мониторе.</summary>
    public void OnActivated()
    {
        if (!IsScanning && !_disposed)
        {
            foreach (var monitor in Monitors)
            {
                _ = monitor.ReadAsync();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        // Гамму вернёт из любого потока, окна затемнения закроет, только если мы в UI-потоке.
        _filters.Dispose();
        if (_dispatcher.CheckAccess())
        {
            foreach (var monitor in Monitors)
            {
                monitor.StopPendingWrites();
            }
        }

        // Не закрывать мониторы посреди команды в фоновом потоке. Не дождались — оставляем: процесс всё равно завершается.
        if (_ddcGate.Wait(DisposeWait))
        {
            DisposeDevices(_devices);
            _ddcGate.Release();
        }
    }

    private static void DisposeDevices(IEnumerable<DdcMonitor> devices)
    {
        foreach (var device in devices)
        {
            device.Dispose();
        }
    }

    /// <summary>
    /// Какому режиму соответствуют текущие значения. Без управляемых мониторов «День» и «Максимум»
    /// совпадают — тогда побеждает последний нажатый. Во время поиска мониторов список пуст,
    /// поэтому режим не пересчитываем, чтобы подсветка не прыгала.
    /// </summary>
    private void OnActivePresetChanged()
    {
        if (IsScanning)
        {
            return;
        }

        var matches = Presets
            .Where(p => p.Value.Dim == DimPercent && p.Value.Warmth == WarmthPercent
                && Monitors.Where(m => m.IsSupported).All(m => m.Brightness == p.Value.Brightness))
            .Select(p => p.Key)
            .ToList();
        _activePreset = _lastAppliedPreset is { } last && matches.Contains(last) ? last : matches.Count > 0 ? matches[0] : null;

        OnPropertyChanged(nameof(ActivePreset));
        foreach (var option in PresetOptions)
        {
            option.Refresh();
        }
    }

    private void OnMonitorsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var monitor in e.NewItems?.OfType<MonitorViewModel>() ?? [])
        {
            monitor.PropertyChanged += OnMonitorPropertyChanged;
        }

        foreach (var monitor in e.OldItems?.OfType<MonitorViewModel>() ?? [])
        {
            monitor.PropertyChanged -= OnMonitorPropertyChanged;
        }

        OnActivePresetChanged();
    }

    private void OnMonitorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MonitorViewModel.Brightness) or nameof(MonitorViewModel.IsSupported))
        {
            OnActivePresetChanged();
        }
    }

    private void ApplyBrightness(int percent)
    {
        foreach (var monitor in Monitors)
        {
            if (monitor.IsSupported)
            {
                monitor.ApplyBrightness(percent);
            }
        }
    }

    private async Task<bool> RunDdcAsync(Func<bool> command)
    {
        await _ddcGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return !_disposed && await Task.Run(command).ConfigureAwait(false);
        }
        finally
        {
            _ddcGate.Release();
        }
    }

    /// <summary>Заново найти мониторы (при старте и после подключения/отключения экранов).</summary>
    private async Task RescanAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (IsScanning)
        {
            // Экраны поменялись ещё раз посреди поиска — повторим, когда он закончится.
            _rescanRequested = true;
            return;
        }

        IsScanning = true;
        try
        {
            await ReplaceMonitorsAsync().ConfigureAwait(true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            Log.Warn("Не удалось найти мониторы", e);
        }
        finally
        {
            IsScanning = false;
        }

        if (_disposed)
        {
            return;
        }

        if (_pendingPresetBrightness is { } brightness)
        {
            _pendingPresetBrightness = null;
            ApplyBrightness(brightness);
        }

        if (_rescanRequested)
        {
            _rescanRequested = false;
            await RescanAsync().ConfigureAwait(true);
        }
    }

    private async Task ReplaceMonitorsAsync()
    {
        List<DdcMonitor> found = [];
        await RunDdcAsync(() =>
        {
            found = DdcMonitor.EnumerateAll();
            return true;
        }).ConfigureAwait(true);

        if (_disposed)
        {
            DisposeDevices(found);
            return;
        }

        // Досылаем то, что пользователь успел подвинуть, пока старые дескрипторы ещё живы.
        foreach (var monitor in Monitors)
        {
            await monitor.FlushAsync().ConfigureAwait(true);
        }

        foreach (var monitor in Monitors)
        {
            monitor.PropertyChanged -= OnMonitorPropertyChanged;
        }

        Monitors.Clear();
        var old = _devices;
        _devices = found;
        await _ddcGate.WaitAsync().ConfigureAwait(true);
        try
        {
            DisposeDevices(old);
        }
        finally
        {
            _ddcGate.Release();
        }

        var models = found.Select(d => new MonitorViewModel(d, RunDdcAsync)).ToList();
        await Task.WhenAll(models.Select(m => m.ReadAsync(probe: true))).ConfigureAwait(true);

        // Сначала те, что управляются; виртуальные дисплеи без DDC/CI — в конце.
        foreach (var model in models.OrderByDescending(m => m.IsSupported))
        {
            Monitors.Add(model);
        }
    }

    /// <summary>События SystemEvents приходят в своём потоке; фильтры живут в UI-потоке.</summary>
    private void OnUiThread(Action action) =>
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                action();
            }
        });

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        OnUiThread(() =>
        {
            _filters.Refresh();
            _ = RescanAsync();
        });

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            OnUiThread(() => WarmthRejected = !_filters.ReapplyWarmth());
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            OnUiThread(() => WarmthRejected = !_filters.ReapplyWarmth());
        }
    }
}
