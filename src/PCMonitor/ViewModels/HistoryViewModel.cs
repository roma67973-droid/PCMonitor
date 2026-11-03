using PCMonitor.Core;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>Страница «История»: график за выбранный период и сводка (пики, перегрев, средняя нагрузка).</summary>
public sealed class HistoryViewModel : ObservableObject
{
    private const int RefreshEveryTicks = 10;
    private const int DayMinutes = 24 * 60;

    private readonly TemperatureHistory _history;
    private int _ticks;
    private bool _isActive;
    private int _rangeMinutes = 60;
    private bool _showLoad;
    private IReadOnlyList<HistoryPoint> _points = [];

    private string _cpuPeakText = Units.Missing;
    private string _cpuPeakDetails = string.Empty;
    private Severity _cpuPeakSeverity;
    private string _gpuPeakText = Units.Missing;
    private string _gpuPeakDetails = string.Empty;
    private Severity _gpuPeakSeverity;
    private string _overheatText = Units.Missing;
    private string _overheatDetails = string.Empty;
    private Severity _overheatSeverity;
    private string _averageLoadText = Units.Missing;

    public HistoryViewModel(TemperatureHistory history) => _history = history;

    /// <summary>Страница видна: только тогда тратим время на пересчёт.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value) && value)
            {
                Refresh();
            }
        }
    }

    public int RangeMinutes
    {
        get => _rangeMinutes;
        set
        {
            if (SetProperty(ref _rangeMinutes, value))
            {
                Refresh();
            }
        }
    }

    public bool ShowLoad { get => _showLoad; set => SetProperty(ref _showLoad, value); }

    public IReadOnlyList<HistoryPoint> Points { get => _points; private set => SetProperty(ref _points, value); }

    public string CpuPeakText { get => _cpuPeakText; private set => SetProperty(ref _cpuPeakText, value); }

    public string CpuPeakDetails { get => _cpuPeakDetails; private set => SetProperty(ref _cpuPeakDetails, value); }

    public Severity CpuPeakSeverity { get => _cpuPeakSeverity; private set => SetProperty(ref _cpuPeakSeverity, value); }

    public string GpuPeakText { get => _gpuPeakText; private set => SetProperty(ref _gpuPeakText, value); }

    public string GpuPeakDetails { get => _gpuPeakDetails; private set => SetProperty(ref _gpuPeakDetails, value); }

    public Severity GpuPeakSeverity { get => _gpuPeakSeverity; private set => SetProperty(ref _gpuPeakSeverity, value); }

    public string OverheatText { get => _overheatText; private set => SetProperty(ref _overheatText, value); }

    public string OverheatDetails { get => _overheatDetails; private set => SetProperty(ref _overheatDetails, value); }

    public Severity OverheatSeverity { get => _overheatSeverity; private set => SetProperty(ref _overheatSeverity, value); }

    public string AverageLoadText { get => _averageLoadText; private set => SetProperty(ref _averageLoadText, value); }

    /// <summary>Вызывается на каждом опросе датчиков; сам решает, пора ли пересчитать.</summary>
    public void OnTick()
    {
        if (_isActive && ++_ticks % RefreshEveryTicks == 0)
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        var points = _history.GetRange(_rangeMinutes);
        Points = points;

        var stats = HistoryStatistics.Compute(points, Thermals.CpuOverheat);

        (CpuPeakText, CpuPeakDetails, CpuPeakSeverity) = DescribePeak(stats.CpuPeak, stats.CpuAverage, Thermals.Cpu);
        (GpuPeakText, GpuPeakDetails, GpuPeakSeverity) = DescribePeak(stats.GpuPeak, stats.GpuAverage, Thermals.Gpu);

        if (stats.CpuSampleMinutes == 0)
        {
            (OverheatText, OverheatDetails, OverheatSeverity) = (Units.Missing, "нет данных", Severity.Normal);
        }
        else
        {
            var share = stats.OverheatMinutes * 100.0 / stats.CpuSampleMinutes;
            OverheatText = Units.Minutes(stats.OverheatMinutes);
            OverheatDetails = $"{share:0}% времени CPU ≥ {Thermals.CpuOverheat:0}°C";
            OverheatSeverity = stats.OverheatMinutes == 0 ? Severity.Normal : share < 10 ? Severity.Elevated : Severity.Critical;
        }

        AverageLoadText = points.Count == 0
            ? Units.Missing
            : $"{Units.Percent(stats.CpuLoadAverage)} / {Units.Percent(stats.GpuLoadAverage)}";
    }

    private (string Text, string Details, Severity Severity) DescribePeak(
        (double Value, DateTime At)? peak, double? average, Thresholds thresholds)
    {
        if (peak is not { } p)
        {
            return (Units.Missing, "нет данных", Severity.Normal);
        }

        var when = _rangeMinutes > DayMinutes ? p.At.ToString("dd.MM в HH:mm", System.Globalization.CultureInfo.CurrentCulture) : $"в {p.At:HH:mm}";
        return (Units.Temperature(p.Value), $"{when} · среднее {Units.Temperature(average)}", thresholds.Classify(p.Value));
    }
}
