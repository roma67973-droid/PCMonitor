namespace PCMonitor.Core;

/// <summary>Сводка по отрезку истории. Отдельно от UI, чтобы покрываться тестами.</summary>
public sealed record HistoryStatistics(
    (double Value, DateTime At)? CpuPeak,
    double? CpuAverage,
    (double Value, DateTime At)? GpuPeak,
    double? GpuAverage,
    int CpuSampleMinutes,
    int OverheatMinutes,
    double CpuLoadAverage,
    double GpuLoadAverage)
{
    public static HistoryStatistics Compute(IReadOnlyList<HistoryPoint> points, double overheatThreshold)
    {
        ArgumentNullException.ThrowIfNull(points);

        var cpu = points.Where(p => p.HasCpuTemp).ToList();
        var gpu = points.Where(p => p.HasGpuTemp).ToList();

        (double, DateTime)? cpuPeak = null;
        if (cpu.Count > 0)
        {
            var max = cpu.MaxBy(p => p.CpuTempMax);
            cpuPeak = (max.CpuTempMax, max.LocalTime);
        }

        (double, DateTime)? gpuPeak = null;
        if (gpu.Count > 0)
        {
            var max = gpu.MaxBy(p => p.GpuTempMax);
            gpuPeak = (max.GpuTempMax, max.LocalTime);
        }

        return new HistoryStatistics(
            cpuPeak,
            cpu.Count > 0 ? cpu.Average(p => p.CpuTemp) : null,
            gpuPeak,
            gpu.Count > 0 ? gpu.Average(p => p.GpuTemp) : null,
            cpu.Count,
            cpu.Count(p => p.CpuTempMax >= overheatThreshold),
            points.Count > 0 ? points.Average(p => p.CpuLoad) : 0,
            points.Count > 0 ? points.Average(p => p.GpuLoad) : 0);
    }
}
