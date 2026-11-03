using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PCMonitor.Core;
using PCMonitor.Hardware;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>
/// Один кулер: показания и пользовательская настройка. Настройка пишется прямо в <see cref="AppSettings"/>,
/// откуда её читает <see cref="FanController"/> в потоке датчиков.
/// </summary>
public sealed class FanViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<TemperatureSourceViewModel> _sources;
    private readonly Action _requestRefresh;
    private readonly string _driverName;

    private float? _rpm;
    private float? _percent;
    private double _currentTemperature = double.NaN;

    public FanViewModel(
        FanReading reading,
        AppSettings settings,
        ObservableCollection<TemperatureSourceViewModel> sources,
        Action requestRefresh)
    {
        ArgumentNullException.ThrowIfNull(reading);

        _settings = settings;
        _sources = sources;
        _requestRefresh = requestRefresh;
        _driverName = reading.Name;
        Key = reading.Key;
        Location = reading.Location;
        Hardware = reading.Hardware;
        IsControllable = reading.IsControllable;

        ResetCurveCommand = new RelayCommand(() => ReplaceCurve(FanCurve.Default()));
        CurveEditedCommand = new RelayCommand(OnCurveEdited);
    }

    public string Key { get; }

    public FanLocation Location { get; }

    public string Hardware { get; }

    public bool IsControllable { get; }

    public ObservableCollection<TemperatureSourceViewModel> TemperatureSources => _sources;

    public ICommand ResetCurveCommand { get; }

    /// <summary>Пользователь отпустил точку в редакторе кривой.</summary>
    public ICommand CurveEditedCommand { get; }

    public string Name
    {
        get => _settings.FanNames.TryGetValue(Key, out var custom) && !string.IsNullOrWhiteSpace(custom) ? custom : _driverName;
        set
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == _driverName)
            {
                _settings.FanNames.TryRemove(Key, out _);
            }
            else
            {
                _settings.FanNames[Key] = trimmed;
            }

            _settings.SaveSoon();
            OnPropertyChanged();
        }
    }

    /// <summary>Обороты для анимации иконки; 0 — кулер стоит.</summary>
    public double Rpm => _rpm ?? 0;

    public string RpmText => _rpm is { } rpm ? rpm.ToString("0", CultureInfo.CurrentCulture) : Units.Missing;

    public double PowerPercent => _percent ?? 0;

    public string PowerText => Units.Percent(_percent);

    public FanMode Mode
    {
        get => ExistingConfig?.Mode ?? FanMode.Auto;
        set
        {
            if (Mode == value)
            {
                return;
            }

            var config = Config;
            config.Mode = value;
            config.Source ??= DefaultSource();
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeDescription));
        }
    }

    public string ModeDescription => Mode switch
    {
        FanMode.Fixed => "Фиксированная скорость",
        FanMode.Curve => "По кривой температуры",
        _ => Location == FanLocation.Gpu ? "Управляет драйвер видеокарты" : "Управляет BIOS материнской платы",
    };

    public double FixedPercent
    {
        get => ExistingConfig?.Fixed ?? 50;
        set
        {
            Config.Fixed = Math.Round(value);
            Commit();
            OnPropertyChanged();
        }
    }

    public string? Source
    {
        get => ExistingConfig?.Source ?? DefaultSource();
        set
        {
            if (value is null || value == Source)
            {
                return;
            }

            Config.Source = value;
            Commit();
            OnPropertyChanged();
            RefreshCurrentTemperature();
        }
    }

    /// <summary>Точки кривой. Редактор графика меняет их на месте и затем вызывает <see cref="CurveEditedCommand"/>.</summary>
    public List<CurvePoint> Curve => Config.Curve;

    /// <summary>Текущая температура выбранного датчика — отмечается на графике кривой.</summary>
    public double CurrentTemperature { get => _currentTemperature; private set => SetProperty(ref _currentTemperature, value); }

    private FanConfig? ExistingConfig => _settings.Fans.TryGetValue(Key, out var config) ? config : null;

    private FanConfig Config => _settings.Fans.GetOrAdd(Key, _ => new FanConfig { Source = DefaultSource() });

    public void Update(FanReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (_rpm != reading.Rpm)
        {
            _rpm = reading.Rpm;
            OnPropertyChanged(nameof(Rpm));
            OnPropertyChanged(nameof(RpmText));
        }

        if (_percent != reading.Percent)
        {
            _percent = reading.Percent;
            OnPropertyChanged(nameof(PowerPercent));
            OnPropertyChanged(nameof(PowerText));
        }

        RefreshCurrentTemperature();
    }

    /// <summary>Применить пресет (используется кнопками «Все — авто / Тихий / Максимум»).</summary>
    public void ApplyPreset(FanMode mode, double fixedPercent, IReadOnlyList<CurvePoint>? curve)
    {
        var config = Config;
        if (curve is not null)
        {
            config.Curve = curve.Select(p => p.Clone()).ToList();
        }

        if (mode == FanMode.Fixed)
        {
            config.Fixed = fixedPercent;
        }

        config.Mode = mode;
        config.Source ??= DefaultSource();
        Commit();
        RaiseConfigChanged();
    }

    /// <summary>Настройки изменились снаружи (например, «Все кулеры — авто» из трея).</summary>
    public void RaiseConfigChanged()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(ModeDescription));
        OnPropertyChanged(nameof(FixedPercent));
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(Curve));
    }

    private void ReplaceCurve(List<CurvePoint> curve)
    {
        Config.Curve = curve;
        Commit();
        OnPropertyChanged(nameof(Curve));
    }

    private void OnCurveEdited() => Commit();

    private void Commit()
    {
        _settings.SaveSoon();
        _requestRefresh();
    }

    private void RefreshCurrentTemperature()
    {
        var id = Source;
        CurrentTemperature = _sources.FirstOrDefault(s => s.Id == id)?.Value ?? double.NaN;
    }

    /// <summary>Датчик по умолчанию: «Package»/«Core» своего устройства, иначе любой подходящий.</summary>
    private string? DefaultSource()
    {
        var group = Location == FanLocation.Gpu ? SensorGroup.Gpu : SensorGroup.Cpu;
        var own = _sources.Where(s => s.Group == group).ToList();
        return (own.Find(s => s.Label.Contains("Package", StringComparison.Ordinal))
                ?? own.Find(s => s.Label.Contains("Core", StringComparison.Ordinal) || s.Label.Contains("Tctl", StringComparison.Ordinal))
                ?? own.FirstOrDefault()
                ?? _sources.FirstOrDefault())?.Id;
    }
}

public sealed class TemperatureSourceViewModel(string id, string label, SensorGroup group) : ObservableObject
{
    private double _value;

    public string Id { get; } = id;

    public string Label { get; } = label;

    public SensorGroup Group { get; } = group;

    public double Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string Display => $"{Label}  —  {Units.Temperature(Value)}";
}
