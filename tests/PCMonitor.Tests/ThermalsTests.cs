using PCMonitor.Core;

namespace PCMonitor.Tests;

public class ThermalsTests
{
    [Theory]
    [InlineData(null, Severity.Normal)]
    [InlineData(40.0, Severity.Normal)]
    [InlineData(74.9, Severity.Normal)]
    [InlineData(75.0, Severity.Elevated)]
    [InlineData(84.9, Severity.Elevated)]
    [InlineData(85.0, Severity.Critical)]
    [InlineData(100.0, Severity.Critical)]
    public void Classify_UsesInclusiveLowerBounds(double? celsius, Severity expected) =>
        Assert.Equal(expected, new Thresholds(Warm: 75, Hot: 85).Classify(celsius));

    [Fact]
    public void EmergencyThresholds_AreAboveHotThresholds()
    {
        // Аварийный разгон кулеров должен включаться позже, чем интерфейс покажет «горячо».
        Assert.True(Thermals.EmergencyCpu > Thermals.Cpu.Hot);
        Assert.True(Thermals.EmergencyGpu > Thermals.Gpu.Hot);
    }
}
