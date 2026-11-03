using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PCMonitor.Core;
using PCMonitor.Hardware;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>Страница «Обзор»: CPU, GPU, ОЗУ, сеть, накопители. Также питает мини-сводку в боковой панели.</summary>
public sealed class OverviewViewModel : ObservableObject
{
    private const int SparklineLength = 60;

    private readonly RingBuffer _cpuHistory = new(SparklineLength);
    private readonly RingBuffer _gpuHistory = new(SparklineLength);
    private readonly RingBuffer _ramHistory = new(SparklineLength);
    private readonly RingBuffer _downHistory = new(SparklineLength);
    private readonly RingBuffer _upHistory = new(SparklineLength);

    private string _cpuName = "Процессор";
    private double _cpuLoad;
    private string _cpuTempText = Units.Missing;
    private Severity _cpuTempSeverity;
    private string _cpuClockText = Units.Missing;
    private string _cpuPowerText = Units.Missing;
    private double[] _cpuSparkline = [];

    private bool _hasGpu;
    private string _gpuName = "Видеокарта";
    private double _gpuLoad;
    private string _gpuTempText = Units.Missing;
    private Severity _gpuTempSeverity;
    private string _gpuHotSpotText = Units.Missing;
    private string _gpuClockText = Units.Missing;
    private string _gpuMemoryClockText = Units.Missing;
    private string _gpuPowerText = Units.Missing;
    private string _gpuFanText = Units.Missing;
    private string _vramText = Units.Missing;
    private double _vramLoad;
    private double[] _gpuSparkline = [];

    private double _ramLoad;
    private string _ramUsedText = Units.Missing;
    private string _ramFreeText = Units.Missing;
    private double[] _ramSparkline = [];

    private string _netDownText = Units.Missing;
    private string _netUpText = Units.Missing;
    private double[] _netDownSparkline = [];
    private double[] _netUpSparkline = [];

    public OverviewViewModel(SystemStatusViewModel status, FansViewModel fans, ICommand openFansCommand)
    {
        Status = status;
        Fans = fans;
        OpenFansCommand = openFansCommand;
    }

    public SystemStatusViewModel Status { get; }

    /// <summary>Для краткого списка кулеров на обзоре.</summary>
    public FansViewModel Fans { get; }

    public ICommand OpenFansCommand { get; }

    public string ThreadCountText => Environment.ProcessorCount.ToString(CultureInfo.CurrentCulture);

    public ObservableCollection<CoreLoadViewModel> Cores { get; } = [];

    public ObservableCollection<DiskViewModel> Disks { get; } = [];

    public string CpuName { get => _cpuName; private set => SetProperty(ref _cpuName, value); }

    public double CpuLoad { get => _cpuLoad; private set => SetProperty(ref _cpuLoad, value); }

    public string CpuTempText { get => _cpuTempText; private set => SetProperty(ref _cpuTempText, value); }

    public Severity CpuTempSeverity { get => _cpuTempSeverity; private set => SetProperty(ref _cpuTempSeverity, value); }

    public string CpuClockText { get => _cpuClockText; private set => SetProperty(ref _cpuClockText, value); }

    public string CpuPowerText { get => _cpuPowerText; private set => SetProperty(ref _cpuPowerText, value); }

    public double[] CpuSparkline { get => _cpuSparkline; private set => SetProperty(ref _cpuSparkline, value); }

    public bool HasGpu { get => _hasGpu; private set => SetProperty(ref _hasGpu, value); }

    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }

    public double GpuLoad { get => _gpuLoad; private set => SetProperty(ref _gpuLoad, value); }

    public string GpuTempText { get => _gpuTempText; private set => SetProperty(ref _gpuTempText, value); }

    public Severity GpuTempSeverity { get => _gpuTempSeverity; private set => SetProperty(ref _gpuTempSeverity, value); }

    public string GpuHotSpotText { get => _gpuHotSpotText; private set => SetProperty(ref _gpuHotSpotText, value); }

    public string GpuClockText { get => _gpuClockText; private set => SetProperty(ref _gpuClockText, value); }

    public string GpuMemoryClockText { get => _gpuMemoryClockText; private set => SetProperty(ref _gpuMemoryClockText, value); }

    public string GpuPowerText { get => _gpuPowerText; private set => SetProperty(ref _gpuPowerText, value); }

    public string GpuFanText { get => _gpuFanText; private set => SetProperty(ref _gpuFanText, value); }

    public string VramText { get => _vramText; private set => SetProperty(ref _vramText, value); }

    public double VramLoad { get => _vramLoad; private set => SetProperty(ref _vramLoad, value); }

    public double[] GpuSparkline { get => _gpuSparkline; private set => SetProperty(ref _gpuSparkline, value); }

    public double RamLoad { get => _ramLoad; private set => SetProperty(ref _ramLoad, value); }

    public string RamUsedText { get => _ramUsedText; private set => SetProperty(ref _ramUsedText, value); }

    public string RamFreeText { get => _ramFreeText; private set => SetProperty(ref _ramFreeText, value); }

    public double[] RamSparkline { get => _ramSparkline; private set => SetProperty(ref _ramSparkline, value); }

    public string NetDownText { get => _netDownText; private set => SetProperty(ref _netDownText, value); }

    public string NetUpText { get => _netUpText; private set => SetProperty(ref _netUpText, value); }

    public double[] NetDownSparkline { get => _netDownSparkline; private set => SetProperty(ref _netDownSparkline, value); }

    public double[] NetUpSparkline { get => _netUpSparkline; private set => SetProperty(ref _netUpSparkline, value); }

    public void Update(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);

        UpdateCpu(s);
        UpdateGpu(s);
        UpdateMemory(s);
        UpdateNetwork(s);
        UpdateDisks(s);
    }

    private static double[] Push(RingBuffer buffer, double value)
    {
        buffer.Add(value);
        return buffer.ToArray();
    }

    private void UpdateCpu(Snapshot s)
    {
        CpuName = string.IsNullOrEmpty(s.CpuName) ? "Процессор" : s.CpuName;
        CpuLoad = s.CpuLoad;
        CpuTempText = Units.Temperature(s.CpuTemp);
        CpuTempSeverity = Thermals.Cpu.Classify(s.CpuTemp);
        CpuClockText = Units.Gigahertz(s.CpuClockMhz);
        CpuPowerText = Units.Watts(s.CpuPower);
        CpuSparkline = Push(_cpuHistory, s.CpuLoad);

        if (Cores.Count != s.CoreLoads.Count)
        {
            Cores.Clear();
            for (var i = 0; i < s.CoreLoads.Count; i++)
            {
                Cores.Add(new CoreLoadViewModel(i + 1));
            }
        }

        for (var i = 0; i < s.CoreLoads.Count; i++)
        {
            Cores[i].Load = s.CoreLoads[i];
        }
    }

    private void UpdateGpu(Snapshot s)
    {
        HasGpu = s.HasGpu;
        if (!s.HasGpu)
        {
            return;
        }

        GpuName = s.GpuName;
        GpuLoad = s.GpuLoad;
        GpuTempText = Units.Temperature(s.GpuTemp);
        GpuTempSeverity = Thermals.Gpu.Classify(s.GpuTemp);
        GpuHotSpotText = Units.Temperature(s.GpuHotSpot);
        GpuClockText = Units.Megahertz(s.GpuClockMhz);
        GpuMemoryClockText = Units.Megahertz(s.GpuMemoryClockMhz);
        GpuPowerText = Units.Watts(s.GpuPower);
        GpuFanText = Units.Rpm(s.GpuFanRpm);
        if (s.VramUsedMb is { } used && s.VramTotalMb is { } total && total > 0)
        {
            VramText = string.Format(CultureInfo.CurrentCulture, "{0:0.0} / {1:0.0} ГБ", used / 1024, total / 1024);
            VramLoad = used / total * 100;
        }

        GpuSparkline = Push(_gpuHistory, s.GpuLoad);
    }

    private void UpdateMemory(Snapshot s)
    {
        RamLoad = s.RamLoad;
        RamUsedText = string.Format(CultureInfo.CurrentCulture, "{0:0.0} / {1:0.0} ГБ", s.RamUsedGb, s.RamTotalGb);
        RamFreeText = string.Format(CultureInfo.CurrentCulture, "{0:0.0} ГБ", s.RamTotalGb - s.RamUsedGb);
        RamSparkline = Push(_ramHistory, s.RamLoad);
    }

    private void UpdateNetwork(Snapshot s)
    {
        NetDownText = Units.TransferRate(s.NetDownBytesPerSec);
        NetUpText = Units.TransferRate(s.NetUpBytesPerSec);
        NetDownSparkline = Push(_downHistory, s.NetDownBytesPerSec);
        NetUpSparkline = Push(_upHistory, s.NetUpBytesPerSec);
    }

    private void UpdateDisks(Snapshot s)
    {
        foreach (var reading in s.Disks)
        {
            var disk = Disks.FirstOrDefault(d => d.Name == reading.Name);
            if (disk is null)
            {
                disk = new DiskViewModel(reading.Name);
                Disks.Add(disk);
            }

            disk.Update(reading);
        }
    }
}

public sealed class CoreLoadViewModel(int number) : ObservableObject
{
    private double _load;

    public string Label { get; } = number.ToString(CultureInfo.CurrentCulture);

    public double Load { get => _load; set => SetProperty(ref _load, value); }
}

public sealed class DiskViewModel(string name) : ObservableObject
{
    private string _temperatureText = Units.Missing;
    private Severity _temperatureSeverity;
    private double _usedPercent;
    private string _usedText = Units.Missing;
    private string _activityText = Units.Missing;

    public string Name { get; } = name;

    public string TemperatureText { get => _temperatureText; private set => SetProperty(ref _temperatureText, value); }

    public Severity TemperatureSeverity { get => _temperatureSeverity; private set => SetProperty(ref _temperatureSeverity, value); }

    public double UsedPercent { get => _usedPercent; private set => SetProperty(ref _usedPercent, value); }

    public string UsedText { get => _usedText; private set => SetProperty(ref _usedText, value); }

    public string ActivityText { get => _activityText; private set => SetProperty(ref _activityText, value); }

    public void Update(DiskReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        TemperatureText = Units.Temperature(reading.Temperature);
        TemperatureSeverity = Thermals.Disk.Classify(reading.Temperature);
        UsedPercent = reading.UsedPercent ?? 0;
        UsedText = reading.UsedPercent is { } used ? $"{Units.Percent(used)} занято" : Units.Missing;
        ActivityText = Units.Percent(reading.ActivityPercent);
    }
}
