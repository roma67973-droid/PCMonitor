using System.IO;
using PCMonitor.Core;
using PCMonitor.Hardware;

namespace PCMonitor.Tests;

public sealed class TemperatureHistoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pcmonitor-history-{Guid.NewGuid():N}.bin");
    private DateTimeOffset _now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    [Fact]
    public void Add_WithinOneMinute_AveragesAndKeepsPeak()
    {
        var history = Create();
        history.Add(Snapshot(cpuTemp: 60, cpuLoad: 10));
        history.Add(Snapshot(cpuTemp: 80, cpuLoad: 30));

        var point = Assert.Single(history.GetRange(60));
        Assert.Equal(70, point.CpuTemp);
        Assert.Equal(80, point.CpuTempMax);
        Assert.Equal(20, point.CpuLoad);
    }

    [Fact]
    public void Add_AcrossMinutes_CreatesOnePointPerMinute()
    {
        var history = Create();
        for (var minute = 0; minute < 5; minute++)
        {
            history.Add(Snapshot(cpuTemp: 50 + minute));
            _now = _now.AddMinutes(1);
        }

        var points = history.GetRange(60);
        Assert.Equal(5, points.Count);
        Assert.Equal([50f, 51f, 52f, 53f, 54f], points.Select(p => p.CpuTemp));
    }

    [Fact]
    public void Add_WithoutGpuTemperature_StoresNaN()
    {
        var history = Create();
        history.Add(Snapshot(cpuTemp: 50, gpuTemp: null));

        var point = Assert.Single(history.GetRange(60));
        Assert.False(point.HasGpuTemp);
        Assert.True(point.HasCpuTemp);
    }

    [Fact]
    public void GetRange_ExcludesOlderPoints()
    {
        var history = Create();
        history.Add(Snapshot(cpuTemp: 40));
        _now = _now.AddMinutes(90);
        history.Add(Snapshot(cpuTemp: 60));

        var point = Assert.Single(history.GetRange(60));
        Assert.Equal(60, point.CpuTemp);
        Assert.Equal(2, history.GetRange(120).Count);
    }

    [Fact]
    public void Flush_ThenReload_RestoresPoints()
    {
        var history = Create();
        history.Add(Snapshot(cpuTemp: 55, gpuTemp: 40));
        _now = _now.AddMinutes(1);
        history.Add(Snapshot(cpuTemp: 65, gpuTemp: 45));
        history.Flush();

        var reloaded = Create();
        var points = reloaded.GetRange(60);
        Assert.Equal(2, points.Count);
        Assert.Equal(45, points[1].GpuTemp);
    }

    [Fact]
    public void Load_DropsPointsOlderThanRetention()
    {
        var history = Create();
        history.Add(Snapshot(cpuTemp: 50));
        history.Flush();

        _now = _now.Add(TemperatureHistory.Retention).AddMinutes(5);
        Assert.Empty(Create().GetRange(int.MaxValue / 2));
    }

    [Fact]
    public void Load_CorruptedFile_StartsEmpty()
    {
        File.WriteAllBytes(_path, [1, 0, 0, 0, 99, 0]);
        Assert.Empty(Create().GetRange(60));
    }

    private TemperatureHistory Create() => new(_path, () => _now);

    private static Snapshot Snapshot(float? cpuTemp = null, float? gpuTemp = null, float cpuLoad = 0) => new()
    {
        CpuTemp = cpuTemp,
        GpuTemp = gpuTemp,
        CpuLoad = cpuLoad,
    };
}
