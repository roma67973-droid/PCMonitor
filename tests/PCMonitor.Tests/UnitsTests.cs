using System.Globalization;
using PCMonitor.Core;

namespace PCMonitor.Tests;

public sealed class UnitsTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public UnitsTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    [Theory]
    [InlineData(0, "0 Б/с")]
    [InlineData(512, "512 Б/с")]
    [InlineData(2048, "2 КБ/с")]
    [InlineData(1.5 * 1024 * 1024, "1,5 МБ/с")]
    public void TransferRate_PicksReadableUnit(double bytesPerSecond, string expected) =>
        Assert.Equal(expected, Units.TransferRate(bytesPerSecond));

    [Theory]
    [InlineData(0, "0 мин")]
    [InlineData(59, "59 мин")]
    [InlineData(60, "1 ч 0 мин")]
    [InlineData(135, "2 ч 15 мин")]
    public void Minutes_SwitchesToHoursAfterSixty(int minutes, string expected) =>
        Assert.Equal(expected, Units.Minutes(minutes));

    [Fact]
    public void Uptime_ShowsDaysForLongIntervals() =>
        Assert.Equal("2 д 3 ч", Units.Uptime(new TimeSpan(2, 3, 40, 0)));

    [Fact]
    public void MissingValues_RenderAsDash()
    {
        Assert.Equal(Units.Missing, Units.Temperature(null));
        Assert.Equal(Units.Missing, Units.Rpm(null));
        Assert.Equal(Units.Missing, Units.Gigahertz(null));
    }

    [Fact]
    public void Gigahertz_ConvertsFromMegahertz() =>
        Assert.Equal("4,70 ГГц", Units.Gigahertz(4700));
}
