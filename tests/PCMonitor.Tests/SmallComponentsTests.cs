using System.Globalization;
using System.Windows.Data;
using PCMonitor.Converters;
using PCMonitor.Core;
using PCMonitor.Hardware;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.Tests;

public class RingBufferTests
{
    [Fact]
    public void ToArray_BeforeWrap_ReturnsInInsertionOrder()
    {
        var buffer = new RingBuffer(5);
        buffer.Add(1);
        buffer.Add(2);
        Assert.Equal([1d, 2d], buffer.ToArray());
    }

    [Fact]
    public void ToArray_AfterWrap_KeepsLatestOldestFirst()
    {
        var buffer = new RingBuffer(3);
        foreach (var v in new double[] { 1, 2, 3, 4, 5 })
        {
            buffer.Add(v);
        }

        Assert.Equal([3d, 4d, 5d], buffer.ToArray());
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void Constructor_RejectsZeroCapacity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer(0));
}

public class EqualsConverterTests
{
    private readonly EqualsConverter _converter = new();

    [Fact]
    public void Convert_MatchesEnumParameter() =>
        Assert.Equal(true, _converter.Convert(FanMode.Curve, typeof(bool), FanMode.Curve, CultureInfo.InvariantCulture));

    [Fact]
    public void Convert_MatchesNumberGivenAsString() =>
        Assert.Equal(true, _converter.Convert(1000, typeof(bool), "1000", CultureInfo.InvariantCulture));

    [Fact]
    public void ConvertBack_Checked_ReturnsTypedParameter()
    {
        Assert.Equal(FanMode.Fixed, _converter.ConvertBack(true, typeof(FanMode), "Fixed", CultureInfo.InvariantCulture));
        Assert.Equal(500, _converter.ConvertBack(true, typeof(int), "500", CultureInfo.InvariantCulture));
        Assert.Equal(true, _converter.ConvertBack(true, typeof(bool), "True", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_Unchecked_DoesNothing() =>
        Assert.Same(Binding.DoNothing, _converter.ConvertBack(false, typeof(int), "500", CultureInfo.InvariantCulture));
}

public class FanNamesTests
{
    [Theory]
    [InlineData("GPU Fan 1", "Кулер видеокарты 1")]
    [InlineData("CPU Fan", "Кулер процессора")]
    [InlineData("CPU", "Кулер процессора")]
    [InlineData("CPU Optional Fan", "Доп. кулер процессора")]
    [InlineData("System Fan #2", "Корпусной #2")]
    [InlineData("Fan #3", "Вентилятор #3")]
    [InlineData("AIO Pump", "AIO Pump")]
    public void Humanize_TranslatesKnownNames(string driverName, string expected) =>
        Assert.Equal(expected, FanNames.Humanize(driverName));
}

public class CommandLineOptionsTests
{
    [Fact]
    public void Parse_Empty_IsNormalStart()
    {
        var options = CommandLineOptions.Parse([]);
        Assert.False(options.StartInTray);
        Assert.Null(options.SetAutostart);
        Assert.Null(options.ScreenshotPrefix);
    }

    [Fact]
    public void Parse_RecognisesAllSwitches()
    {
        Assert.True(CommandLineOptions.Parse(["--tray"]).StartInTray);
        Assert.True(CommandLineOptions.Parse(["--autostart-on"]).SetAutostart);
        Assert.False(CommandLineOptions.Parse(["--autostart-off"]).SetAutostart);
        Assert.Equal(@"C:\shots\x", CommandLineOptions.Parse(["--screenshots", @"C:\shots\x"]).ScreenshotPrefix);
    }

    [Fact]
    public void Parse_ScreenshotsWithoutValue_IsIgnored() =>
        Assert.Null(CommandLineOptions.Parse(["--screenshots"]).ScreenshotPrefix);
}
