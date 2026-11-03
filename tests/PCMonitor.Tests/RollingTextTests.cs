using PCMonitor.Controls;
using Xunit;

namespace PCMonitor.Tests;

public class RollingTextTests
{
    [Theory]
    [InlineData("63°C", "", 63, 0, "°C")]
    [InlineData("4,60 ГГц", "", 4.6, 2, " ГГц")]
    [InlineData("12.5 ГБ", "", 12.5, 1, " ГБ")]
    [InlineData("CPU 8%", "CPU ", 8, 0, "%")]
    public void Parse_SplitsPrefixNumberSuffix(string text, string prefix, double value, int decimals, string suffix)
    {
        var t = RollingText.Parse(text)!;

        Assert.Equal(prefix, t.Prefix);
        Assert.Equal(value, t.Value, 6);
        Assert.Equal(decimals, t.Decimals);
        Assert.Equal(suffix, t.Suffix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("—")]
    public void Parse_NoNumber_ReturnsNull(string? text) => Assert.Null(RollingText.Parse(text));

    [Fact]
    public void Format_KeepsSeparatorAndPrecision()
    {
        var t = RollingText.Parse("4,60 ГГц")!;

        Assert.Equal("4,73 ГГц", RollingText.Format(t, 4.7312));
    }

    [Fact]
    public void SameShape_DifferentUnits_IsFalse()
    {
        Assert.False(RollingText.Parse("743 Б/с")!.SameShape(RollingText.Parse("1 КБ/с")!));
        Assert.True(RollingText.Parse("63°C")!.SameShape(RollingText.Parse("67°C")!));
    }

    [Fact]
    public void Format_NegativeZero_HasNoMinus()
    {
        var t = RollingText.Parse("3°C")!;

        Assert.Equal("0°C", RollingText.Format(t, -0.3));
    }
}
