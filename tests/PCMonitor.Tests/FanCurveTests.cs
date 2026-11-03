using PCMonitor.Core;

namespace PCMonitor.Tests;

public class FanCurveTests
{
    private static readonly List<CurvePoint> Curve = [new(30, 20), new(50, 40), new(80, 100)];

    [Theory]
    [InlineData(10, 20)]   // левее первой точки — значение первой
    [InlineData(30, 20)]
    [InlineData(40, 30)]   // посередине отрезка 30..50
    [InlineData(50, 40)]
    [InlineData(65, 70)]
    [InlineData(80, 100)]
    [InlineData(95, 100)]  // правее последней точки — значение последней
    public void Evaluate_InterpolatesLinearlyBetweenPoints(double temperature, double expected) =>
        Assert.Equal(expected, FanCurve.Evaluate(Curve, temperature), precision: 6);

    [Fact]
    public void Evaluate_EmptyCurve_ReturnsFullSpeed() =>
        Assert.Equal(100, FanCurve.Evaluate([], 40));

    [Fact]
    public void Evaluate_SinglePoint_IsConstant()
    {
        List<CurvePoint> single = [new(60, 45)];
        Assert.Equal(45, FanCurve.Evaluate(single, 20));
        Assert.Equal(45, FanCurve.Evaluate(single, 90));
    }

    [Fact]
    public void Presets_AreSortedByTemperatureAndEndAtFullSpeed()
    {
        foreach (var preset in new[] { FanCurve.Default(), FanCurve.Quiet() })
        {
            Assert.Equal(preset.OrderBy(p => p.T).Select(p => p.T), preset.Select(p => p.T));
            Assert.Equal(100, preset[^1].P);
        }
    }

    [Fact]
    public void Presets_ReturnIndependentCopies()
    {
        var first = FanCurve.Default();
        first[0].P = 99;
        Assert.NotEqual(99, FanCurve.Default()[0].P);
    }
}
