using System.Globalization;
using PCMonitor.Hardware;

namespace PCMonitor.Tests;

public sealed class SensorRulesTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public SensorRulesTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    [Theory]
    [InlineData("Composite Temperature", 41f, true)]
    [InlineData("Temperature", 35f, true)]
    [InlineData("CPU Package", 70f, true)]
    // Реальный NVMe без рабочего датчика: измерение 0, а «температуры» 69/74 — паспортные пороги.
    [InlineData("Composite Temperature", 0f, false)]
    [InlineData("Warning Temperature", 69f, false)]
    [InlineData("Critical Temperature", 74f, false)]
    [InlineData("Temperature", 255f, false)]
    public void IsTemperatureReading_RejectsThresholdsAndGarbage(string name, float value, bool expected) =>
        Assert.Equal(expected, SensorRules.IsTemperatureReading(name, value));

    [Fact]
    public void IsTemperatureReading_NullValue_IsRejected() =>
        Assert.False(SensorRules.IsTemperatureReading("Temperature", null));

    [Theory]
    [InlineData("Samsung SSD 860 EVO M.2 250GB ", "/hdd/1", 232f, "Samsung SSD 860 EVO M.2 250GB")]
    [InlineData("                                        ", "/nvme/2", 1024.2f, "NVMe-накопитель 1 ТБ")]
    [InlineData("\0\0\0\0", "/nvme/0", 476.9f, "NVMe-накопитель 477 ГБ")]
    [InlineData("", "/hdd/3", null, "Накопитель 4")]
    public void StorageDisplayName_FallsBackToKindAndCapacity(string raw, string id, float? totalGb, string expected) =>
        Assert.Equal(expected, SensorRules.StorageDisplayName(raw, id, totalGb, ordinal: 4));
}
