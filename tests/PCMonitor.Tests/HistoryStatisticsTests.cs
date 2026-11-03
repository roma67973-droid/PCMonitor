using PCMonitor.Core;

namespace PCMonitor.Tests;

public class HistoryStatisticsTests
{
    [Fact]
    public void Compute_Empty_HasNoPeaks()
    {
        var stats = HistoryStatistics.Compute([], overheatThreshold: 85);

        Assert.Null(stats.CpuPeak);
        Assert.Null(stats.GpuPeak);
        Assert.Equal(0, stats.CpuSampleMinutes);
    }

    [Fact]
    public void Compute_FindsPeakAndCountsOverheatMinutes()
    {
        HistoryPoint[] points =
        [
            Point(1, cpu: 60, cpuMax: 70),
            Point(2, cpu: 80, cpuMax: 92),
            Point(3, cpu: 75, cpuMax: 86),
            Point(4, cpu: float.NaN, cpuMax: float.NaN),
        ];

        var stats = HistoryStatistics.Compute(points, overheatThreshold: 85);

        Assert.Equal(92, stats.CpuPeak!.Value.Value);
        Assert.Equal(3, stats.CpuSampleMinutes);
        Assert.Equal(2, stats.OverheatMinutes);
        Assert.Equal(215.0 / 3, stats.CpuAverage!.Value, precision: 4);
    }

    private static HistoryPoint Point(long minute, float cpu, float cpuMax) =>
        new(minute, cpu, cpuMax, GpuTemp: float.NaN, GpuTempMax: float.NaN, CpuLoad: 0, GpuLoad: 0);
}
