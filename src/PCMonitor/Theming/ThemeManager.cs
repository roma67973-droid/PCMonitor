using System.Windows;
using System.Windows.Media;
using PCMonitor.Core;

namespace PCMonitor.Theming;

/// <summary>Палитра приложения. Цвета в формате #RRGGBB.</summary>
public sealed record Palette(
    string Background,
    string Sidebar,
    string Card,
    string CardHover,
    string Border,
    string Text,
    string SubText,
    string Accent,
    string Accent2,
    string Track,
    string Good,
    string Warn,
    string Hot);

/// <summary>Кисти палитры кладутся в ресурсы приложения; разметка ссылается на них через DynamicResource.</summary>
public static class ThemeManager
{
    public static Palette Current { get; } =
        new("#141414", "#0E0E0E", "#1D1D1D", "#252525", "#2F2F2F", "#EEEEEE", "#999999", "#FF8A3D", "#FFD23D", "#2B2B2B", "#4ADE80", "#FACC15", "#F87171");

    public static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static SolidColorBrush CreateBrush(string hex, byte alpha = 255)
    {
        var color = ParseColor(hex);
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Цвет уровня тревожности (для кода, которому не доступны ресурсы WPF).</summary>
    public static string SeverityColor(Severity severity) => severity switch
    {
        Severity.Critical => Current.Hot,
        Severity.Elevated => Current.Warn,
        _ => Current.Accent,
    };

    public static void Apply()
    {
        var p = Current;
        var r = Application.Current.Resources;
        r["BgBrush"] = CreateBrush(p.Background);
        r["SideBrush"] = CreateBrush(p.Sidebar);
        r["CardBrush"] = CreateBrush(p.Card);
        r["CardHoverBrush"] = CreateBrush(p.CardHover);
        r["BorderBrush"] = CreateBrush(p.Border);
        r["TextBrush"] = CreateBrush(p.Text);
        r["SubTextBrush"] = CreateBrush(p.SubText);
        r["AccentBrush"] = CreateBrush(p.Accent);
        r["Accent2Brush"] = CreateBrush(p.Accent2);
        r["AccentSoftBrush"] = CreateBrush(p.Accent, 0x26);
        r["AccentHoverBrush"] = CreateBrush(p.Accent, 0x70);
        r["TrackBrush"] = CreateBrush(p.Track);
        r["ScrollBrush"] = CreateBrush(p.SubText, 0x55);
        r["GoodBrush"] = CreateBrush(p.Good);
        r["WarnBrush"] = CreateBrush(p.Warn);
        r["HotBrush"] = CreateBrush(p.Hot);
        r["WarnSoftBrush"] = CreateBrush(p.Warn, 0x22);
        // Подложка выбранного сегмента или режима: нейтральная, акцентом выделяется только текст.
        r["SelectionBrush"] = CreateBrush(p.Border);
    }
}
