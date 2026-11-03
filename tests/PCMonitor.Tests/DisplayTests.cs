using PCMonitor.Platform;
using Xunit;

namespace PCMonitor.Tests;

public class DisplayTests
{
    [Fact]
    public void ParseEdidName_ReadsMonitorNameDescriptor()
    {
        var edid = new byte[128];
        // Второй дескриптор (смещение 72) — имя монитора, тег 0xFC, текст до перевода строки.
        edid[72 + 3] = 0xFC;
        var name = "Optix G27C2\n "u8.ToArray();
        Array.Copy(name, 0, edid, 72 + 5, name.Length);

        Assert.Equal("Optix G27C2", DdcMonitor.ParseEdidName(edid));
    }

    [Fact]
    public void ParseEdidName_NoNameDescriptor_ReturnsNull()
    {
        var edid = new byte[128];
        edid[54] = 0x1A; // дескриптор тайминга, не имени

        Assert.Null(DdcMonitor.ParseEdidName(edid));
    }

    [Fact]
    public void GammaRamp_ZeroWarmth_IsLinear()
    {
        var ramp = ScreenFilters.BuildGammaRamp(0);

        Assert.Equal(768, ramp.Length);
        Assert.Equal(0, ramp[0]);
        Assert.Equal(65535, ramp[255]);
        Assert.Equal(65535, ramp[511]);
        Assert.Equal(65535, ramp[767]);
    }

    [Fact]
    public void GammaRamp_FullWarmth_KeepsRedAndCutsBlueMoreThanGreen()
    {
        var ramp = ScreenFilters.BuildGammaRamp(100);

        Assert.Equal(65535, ramp[255]);
        Assert.True(ramp[767] < ramp[511]);
        Assert.True(ramp[511] < 65535);
    }

    [Fact]
    public void GammaRamp_AppliesWarmthOnTopOfCalibration()
    {
        var calibration = new ushort[768];
        Array.Fill(calibration, (ushort)40000);

        var ramp = ScreenFilters.BuildGammaRamp(100, calibration);

        Assert.Equal(40000, ramp[10]);       // красный не трогаем
        Assert.Equal(32800, ramp[256 + 10]); // зелёный × 0.82
        Assert.Equal(24800, ramp[512 + 10]); // синий × 0.62
    }
}
