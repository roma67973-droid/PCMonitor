using System.Collections.ObjectModel;
using System.Windows.Input;
using PCMonitor.Core;
using PCMonitor.Hardware;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>Страница «Кулеры»: список кулеров, источники температуры, пресеты для всех сразу.</summary>
public sealed class FansViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _requestRefresh;
    private bool _hasFans;
    private string _summary = string.Empty;

    public FansViewModel(AppSettings settings, SystemStatusViewModel status, Action requestRefresh)
    {
        _settings = settings;
        _requestRefresh = requestRefresh;
        Status = status;

        SetAllAutoCommand = new RelayCommand(() => ApplyToAll(FanMode.Auto, 0, null));
        SetAllQuietCommand = new RelayCommand(() => ApplyToAll(FanMode.Curve, 0, FanCurve.Quiet()));
        SetAllMaxCommand = new RelayCommand(() => ApplyToAll(FanMode.Fixed, 100, null));
    }

    public SystemStatusViewModel Status { get; }

    public ObservableCollection<FanViewModel> Items { get; } = [];

    public ObservableCollection<TemperatureSourceViewModel> TemperatureSources { get; } = [];

    public ICommand SetAllAutoCommand { get; }

    public ICommand SetAllQuietCommand { get; }

    public ICommand SetAllMaxCommand { get; }

    public bool HasFans { get => _hasFans; private set => SetProperty(ref _hasFans, value); }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public void Update(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);

        // Сначала датчики: от них зависит выбор источника по умолчанию у новых кулеров.
        foreach (var reading in s.Temperatures)
        {
            var source = TemperatureSources.FirstOrDefault(t => t.Id == reading.Id);
            if (source is null)
            {
                source = new TemperatureSourceViewModel(reading.Id, reading.Label, reading.Group);
                TemperatureSources.Add(source);
            }

            source.Value = reading.Value;
        }

        var present = new HashSet<string>();
        foreach (var reading in s.Fans)
        {
            present.Add(reading.Key);
            var fan = Items.FirstOrDefault(f => f.Key == reading.Key);
            if (fan is null)
            {
                fan = new FanViewModel(reading, _settings, TemperatureSources, _requestRefresh);
                Items.Add(fan);
            }

            fan.Update(reading);
        }

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!present.Contains(Items[i].Key))
            {
                Items.RemoveAt(i);
            }
        }

        HasFans = Items.Count > 0;
        Summary = $"Найдено: {s.Fans.Count} · крутятся: {s.Fans.Count(f => f.Rpm > 0)} · управляемых: {s.Fans.Count(f => f.IsControllable)}";
    }

    /// <summary>Перевести все кулеры в авто — в том числе те, что сейчас не отображаются.</summary>
    public void ReleaseAll()
    {
        foreach (var config in _settings.Fans.Values)
        {
            config.Mode = FanMode.Auto;
        }

        foreach (var fan in Items)
        {
            fan.RaiseConfigChanged();
        }

        _settings.SaveSoon();
        _requestRefresh();
    }

    private void ApplyToAll(FanMode mode, double fixedPercent, IReadOnlyList<CurvePoint>? curve)
    {
        foreach (var fan in Items.Where(f => f.IsControllable))
        {
            fan.ApplyPreset(mode, fixedPercent, curve);
        }
    }
}
