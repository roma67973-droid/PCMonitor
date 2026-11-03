using System.Diagnostics;
using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;
using PCMonitor.Core;
using PCMonitor.Platform;

namespace PCMonitor.Hardware;

/// <summary>
/// Обёртка над LibreHardwareMonitor: опрашивает железо и собирает <see cref="Snapshot"/>.
/// Не потокобезопасен — используется только из потока датчиков.
/// </summary>
internal sealed class SensorReader : IDisposable
{
    /// <summary>Накопители опрашиваются реже: SMART-запросы дорогие, а значения меняются медленно.</summary>
    private const int StoragePollDivider = 5;

    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware? _gpu;
    private readonly List<IHardware> _gpus = [];
    private readonly List<IHardware> _motherboards = [];
    private readonly List<IHardware> _storages = [];
    private readonly List<IHardware> _coolers = [];
    private readonly Dictionary<string, IControl> _controls = [];
    private readonly Dictionary<string, ISensor> _temperatureSensors = [];
    private readonly AppSettings _settings;

    private long _tick;
    private NetworkCounter _network;

    public SensorReader(AppSettings settings)
    {
        _settings = settings;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
        };
        _computer.Open();

        foreach (var hardware in _computer.Hardware)
        {
            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    _cpu ??= hardware;
                    break;
                case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel:
                    _gpus.Add(hardware);
                    break;
                case HardwareType.Motherboard:
                    _motherboards.Add(hardware);
                    break;
                case HardwareType.Storage:
                    _storages.Add(hardware);
                    break;
                case HardwareType.Cooler or HardwareType.EmbeddedController:
                    _coolers.Add(hardware);
                    break;
            }
        }

        // Дискретная видеокарта приоритетнее встроенной.
        _gpu = _gpus.Find(g => g.HardwareType == HardwareType.GpuNvidia)
            ?? _gpus.Find(g => g.HardwareType == HardwareType.GpuAmd)
            ?? _gpus.FirstOrDefault();

        Log.Info($"Датчики: CPU={_cpu?.Name ?? "нет"}, GPU={_gpu?.Name ?? "нет"}, плат={_motherboards.Count}, дисков={_storages.Count}");
    }

    /// <summary>Управляемые кулеры по ключу. Заполняется при каждом <see cref="Read"/>.</summary>
    public IReadOnlyDictionary<string, IControl> Controls => _controls;

    public float? GetTemperature(string sensorId) =>
        _temperatureSensors.TryGetValue(sensorId, out var sensor) ? sensor.Value : null;

    /// <param name="full">Полный опрос (окно открыто): плюс сеть и диски.</param>
    public Snapshot Read(bool full)
    {
        _tick++;
        if (_cpu is not null)
        {
            Update(_cpu);
        }

        _gpus.ForEach(Update);
        _motherboards.ForEach(Update);
        _coolers.ForEach(Update);
        if (full && _tick % StoragePollDivider == 1)
        {
            _storages.ForEach(Update);
        }

        var cpu = ReadCpu();
        var gpu = ReadGpu();
        var memory = NativeMethods.GetMemoryStatus();
        var temperatures = ReadTemperatures(out var hasBoardSensors);
        var (down, up) = full ? _network.Sample() : default;
        if (!full)
        {
            _network.Reset();
        }

        return new Snapshot
        {
            CpuName = _cpu?.Name ?? string.Empty,
            CpuLoad = cpu.Load,
            CpuTemp = cpu.Temperature,
            CpuClockMhz = cpu.ClockMhz,
            CpuPower = cpu.Power,
            CoreLoads = cpu.CoreLoads,
            HasGpu = _gpu is not null,
            GpuName = _gpu?.Name ?? string.Empty,
            GpuLoad = gpu.Load,
            GpuTemp = gpu.Temperature,
            GpuHotSpot = gpu.HotSpot,
            GpuClockMhz = gpu.ClockMhz,
            GpuMemoryClockMhz = gpu.MemoryClockMhz,
            GpuPower = gpu.Power,
            GpuFanRpm = gpu.FanRpm,
            VramUsedMb = gpu.VramUsedMb,
            VramTotalMb = gpu.VramTotalMb,
            RamTotalGb = memory.TotalPhysical / (double)(1L << 30),
            RamUsedGb = (memory.TotalPhysical - memory.AvailablePhysical) / (double)(1L << 30),
            RamLoad = memory.MemoryLoad,
            NetDownBytesPerSec = down,
            NetUpBytesPerSec = up,
            Disks = full ? ReadDisks() : [],
            Fans = ReadFans(),
            Temperatures = temperatures,
            HasMotherboardSensors = hasBoardSensors,
        };
    }

    public void Dispose() => _computer.Close();

    private static void Update(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
        {
            sub.Update();
        }

        // LHM по умолчанию копит историю каждого датчика — нам она не нужна, а память растёт.
        DisableSensorHistory(hardware);
        foreach (var sub in hardware.SubHardware)
        {
            DisableSensorHistory(sub);
        }
    }

    private static void DisableSensorHistory(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.ValuesTimeWindow != TimeSpan.Zero)
            {
                sensor.ValuesTimeWindow = TimeSpan.Zero;
            }
        }
    }

    private CpuReading ReadCpu()
    {
        if (_cpu is null)
        {
            return new CpuReading();
        }

        float load = 0;
        float? package = null, tctl = null, coreMax = null, anyMax = null, power = null;
        double clockSum = 0;
        var clockCount = 0;
        var cores = new List<(int Index, float Load)>();

        foreach (var sensor in _cpu.Sensors)
        {
            if (sensor.Value is not { } v)
            {
                continue;
            }

            switch (sensor.SensorType)
            {
                case SensorType.Load when sensor.Name == "CPU Total":
                    load = v;
                    break;
                case SensorType.Load when sensor.Name.StartsWith("CPU Core #", StringComparison.Ordinal):
                    cores.Add((sensor.Index, v));
                    break;
                case SensorType.Temperature:
                    if (sensor.Name == "CPU Package")
                    {
                        package = v;
                    }
                    else if (sensor.Name.StartsWith("Core (Tctl", StringComparison.Ordinal))
                    {
                        tctl = v;
                    }
                    else if (sensor.Name == "Core Max")
                    {
                        coreMax = v;
                    }

                    if (SensorRules.IsPlausibleTemperature(v))
                    {
                        anyMax = Math.Max(anyMax ?? 0, v);
                    }

                    break;
                case SensorType.Clock when v > 0 && sensor.Name.Contains("Core #", StringComparison.Ordinal):
                    clockSum += v;
                    clockCount++;
                    break;
                case SensorType.Power when sensor.Name.Contains("Package", StringComparison.Ordinal):
                    power = v;
                    break;
            }
        }

        cores.Sort((a, b) => a.Index.CompareTo(b.Index));
        return new CpuReading
        {
            Load = load,
            // Intel отдаёт Package, AMD — Tctl; на экзотике берём максимум по ядрам.
            Temperature = package ?? tctl ?? coreMax ?? anyMax,
            ClockMhz = clockCount > 0 ? (float)(clockSum / clockCount) : null,
            Power = power,
            CoreLoads = cores.ConvertAll(c => c.Load),
        };
    }

    private GpuReading ReadGpu()
    {
        if (_gpu is null)
        {
            return new GpuReading();
        }

        var r = new GpuReading();
        foreach (var sensor in _gpu.Sensors)
        {
            if (sensor.Value is not { } v)
            {
                continue;
            }

            switch (sensor.SensorType)
            {
                case SensorType.Load when sensor.Name == "GPU Core":
                    r.Load = v;
                    break;
                case SensorType.Temperature when sensor.Name == "GPU Core":
                    r.Temperature = v;
                    break;
                case SensorType.Temperature when sensor.Name.Contains("Hot Spot", StringComparison.Ordinal):
                    r.HotSpot = v;
                    break;
                case SensorType.Clock when sensor.Name == "GPU Core":
                    r.ClockMhz = v;
                    break;
                case SensorType.Clock when sensor.Name == "GPU Memory":
                    r.MemoryClockMhz = v;
                    break;
                case SensorType.SmallData when sensor.Name is "GPU Memory Used" or "D3D Dedicated Memory Used":
                    r.VramUsedMb ??= v;
                    break;
                case SensorType.SmallData when sensor.Name == "GPU Memory Total":
                    r.VramTotalMb = v;
                    break;
                case SensorType.Power when r.Power is null || sensor.Name.Contains("Package", StringComparison.Ordinal):
                    r.Power = v;
                    break;
                case SensorType.Fan:
                    r.FanRpm = Math.Max(r.FanRpm ?? 0, v);
                    break;
            }
        }

        r.Temperature ??= _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature)?.Value;
        return r;
    }

    private List<DiskReading> ReadDisks()
    {
        var disks = new List<DiskReading>(_storages.Count);
        for (var i = 0; i < _storages.Count; i++)
        {
            var disk = _storages[i];
            float? temperature = null, used = null, activity = null;
            foreach (var sensor in disk.Sensors)
            {
                switch (sensor.SensorType)
                {
                    case SensorType.Temperature when SensorRules.IsTemperatureReading(sensor.Name, sensor.Value):
                        temperature ??= sensor.Value;
                        break;
                    case SensorType.Load when sensor.Name == "Used Space":
                        used = sensor.Value;
                        break;
                    case SensorType.Load when sensor.Name == "Total Activity":
                        activity = sensor.Value;
                        break;
                }
            }

            disks.Add(new DiskReading(StorageName(i), temperature, used, activity));
        }

        return disks;
    }

    private List<TemperatureReading> ReadTemperatures(out bool hasMotherboardSensors)
    {
        _temperatureSensors.Clear();
        var result = new List<TemperatureReading>();
        hasMotherboardSensors = false;

        if (_cpu is not null)
        {
            CollectTemperatures(_cpu, "Процессор", SensorGroup.Cpu, result);
        }

        if (_gpu is not null)
        {
            CollectTemperatures(_gpu, "Видеокарта", SensorGroup.Gpu, result);
        }

        foreach (var board in _motherboards)
        {
            hasMotherboardSensors |= board.SubHardware.Any(sub => sub.Sensors.Length > 0);
            CollectTemperatures(board, "Мат. плата", SensorGroup.Motherboard, result);
        }

        foreach (var cooler in _coolers)
        {
            CollectTemperatures(cooler, cooler.Name, SensorGroup.Cooler, result);
        }

        for (var i = 0; i < _storages.Count; i++)
        {
            CollectTemperatures(_storages[i], StorageName(i), SensorGroup.Storage, result);
        }

        return result;
    }

    private void CollectTemperatures(IHardware hardware, string prefix, SensorGroup group, List<TemperatureReading> into)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature || !SensorRules.IsTemperatureReading(sensor.Name, sensor.Value))
            {
                continue;
            }

            var id = sensor.Identifier.ToString();
            _temperatureSensors[id] = sensor;
            into.Add(new TemperatureReading(id, $"{prefix} · {sensor.Name}", sensor.Value!.Value, group));
        }

        foreach (var sub in hardware.SubHardware)
        {
            CollectTemperatures(sub, prefix, group, into);
        }
    }

    private string StorageName(int index)
    {
        var disk = _storages[index];
        var total = disk.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name == "Total Space")?.Value;
        return SensorRules.StorageDisplayName(disk.Name, disk.Identifier.ToString(), total, index + 1);
    }

    private List<FanReading> ReadFans()
    {
        var fans = new List<FanReading>();
        foreach (var gpu in _gpus)
        {
            CollectFans(gpu, FanLocation.Gpu, fans);
        }

        foreach (var board in _motherboards)
        {
            CollectFans(board, FanLocation.Motherboard, fans);
        }

        foreach (var cooler in _coolers)
        {
            CollectFans(cooler, FanLocation.Cooler, fans);
        }

        return fans;
    }

    /// <summary>
    /// Сопоставляет датчики оборотов (Fan) и каналы управления (Control): сначала по имени, затем по индексу.
    /// Канал управления без датчика оборотов тоже показываем — им можно управлять вслепую.
    /// </summary>
    private void CollectFans(IHardware hardware, FanLocation location, List<FanReading> into)
    {
        var controls = hardware.Sensors.Where(s => s.SensorType == SensorType.Control).ToList();
        foreach (var fan in hardware.Sensors.Where(s => s.SensorType == SensorType.Fan))
        {
            var control = controls.Find(c => c.Name == fan.Name) ?? controls.Find(c => c.Index == fan.Index);
            if (control is not null)
            {
                controls.Remove(control);
            }

            AddFan(hardware, fan, control, location, into);
        }

        foreach (var control in controls)
        {
            AddFan(hardware, null, control, location, into);
        }

        foreach (var sub in hardware.SubHardware)
        {
            CollectFans(sub, location, into);
        }
    }

    private void AddFan(IHardware hardware, ISensor? fan, ISensor? control, FanLocation location, List<FanReading> into)
    {
        var primary = (control ?? fan)!;
        var key = primary.Identifier.ToString();
        if (control?.Control is { } ctl)
        {
            _controls[key] = ctl;
        }

        // Пустые разъёмы мультиконтроллера материнской платы — шум, а не информация.
        var isEmptyHeader = hardware.HardwareType == HardwareType.SuperIO && fan?.Value is null or < 1;
        if (isEmptyHeader && _settings.HideEmptyFans)
        {
            return;
        }

        into.Add(new FanReading(
            key,
            FanNames.Humanize(primary.Name),
            hardware.HardwareType == HardwareType.SuperIO ? hardware.Parent?.Name ?? hardware.Name : hardware.Name,
            location,
            fan?.Value,
            control?.Value,
            control?.Control is not null));
    }

    private struct CpuReading
    {
        public float Load;
        public float? Temperature;
        public float? ClockMhz;
        public float? Power;
        public IReadOnlyList<float> CoreLoads;
    }

    private struct GpuReading
    {
        public float Load;
        public float? Temperature;
        public float? HotSpot;
        public float? ClockMhz;
        public float? MemoryClockMhz;
        public float? Power;
        public float? FanRpm;
        public float? VramUsedMb;
        public float? VramTotalMb;
    }

    /// <summary>Скорость сети как разница счётчиков между двумя опросами.</summary>
    private struct NetworkCounter
    {
        private long _received;
        private long _sent;
        private long _timestamp;

        public (double Down, double Up) Sample()
        {
            long received = 0, sent = 0;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var stats = adapter.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }

            var now = Stopwatch.GetTimestamp();
            (double, double) rate = default;
            if (_timestamp != 0)
            {
                var seconds = Stopwatch.GetElapsedTime(_timestamp, now).TotalSeconds;
                if (seconds > 0)
                {
                    rate = (Math.Max(0, (received - _received) / seconds), Math.Max(0, (sent - _sent) / seconds));
                }
            }

            (_received, _sent, _timestamp) = (received, sent, now);
            return rate;
        }

        public void Reset() => _timestamp = 0;
    }
}
