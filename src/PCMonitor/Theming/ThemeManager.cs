using System.Windows;
using System.Windows.Media;
using PCMonitor.Core;

namespace PCMonitor.Theming;

/// <summary>Палитра темы. Цвета в формате #RRGGBB.</summary>
public sealed record ThemeDefinition(
    string Name,
    bool IsDark,
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

/// <summary>
/// Каталог тем и их применение: тема — это набор кистей в ресурсах приложения,
/// разметка ссылается на них через DynamicResource и перекрашивается сразу.
/// </summary>
public static class ThemeManager
{
    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new("Полночь", true, "#0D1120", "#090C17", "#151A2D", "#1C2239", "#242B45", "#E9EDF9", "#8790B0", "#00C8FF", "#7C4DFF", "#222A44", "#3DDC97", "#FFB547", "#FF5370"),
        new("Неон", true, "#0B0A12", "#07060C", "#15121F", "#1D1929", "#2B2540", "#F3EEFF", "#9A91B9", "#FF3EA5", "#8C52FF", "#251F38", "#3DF5B0", "#FFC94D", "#FF4D6D"),
        new("Океан", true, "#091622", "#060F18", "#0F202F", "#14293C", "#1C3349", "#E3F1FB", "#86A3BC", "#3EA6FF", "#00E5D4", "#172A3C", "#2EE6A6", "#FFC24D", "#FF5C7A"),
        new("Изумруд", true, "#0B1512", "#07100D", "#12201B", "#172A23", "#20372D", "#E5F5EE", "#84A89A", "#2EE6A6", "#B4F25C", "#1B2F27", "#5CF2A0", "#FFD25C", "#FF6B6B"),
        new("Закат", true, "#190E14", "#12080D", "#23141B", "#2D1A23", "#3A222D", "#FCEFF3", "#B8929F", "#FF6B6B", "#FFB36B", "#331F2A", "#5CE0A0", "#FFC94D", "#FF4D6D"),
        new("Графит", true, "#141414", "#0E0E0E", "#1D1D1D", "#252525", "#2F2F2F", "#EEEEEE", "#999999", "#FF8A3D", "#FFD23D", "#2B2B2B", "#4ADE80", "#FACC15", "#F87171"),
        new("Светлая", false, "#F2F4F9", "#E8ECF4", "#FFFFFF", "#F5F7FC", "#DCE2ED", "#1A2033", "#69738E", "#2F6BFF", "#8B5CF6", "#E5E9F2", "#16A34A", "#D97706", "#E11D48"),
        new("Сакура", false, "#FBF3F6", "#F4E6EC", "#FFFFFF", "#FFF6F9", "#EFD9E2", "#2B1A21", "#8E6B78", "#E84A7F", "#A95CFF", "#F2E2E9", "#16A34A", "#D97706", "#E11D48"),
    ];

    public static ThemeDefinition Current { get; private set; } = All[0];

    public static event Action<ThemeDefinition>? Changed;

    public static ThemeDefinition Find(string name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    public static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Образец темы на странице «Темы»: переход между двумя акцентами палитры.</summary>
    public static LinearGradientBrush CreateAccentGradient(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var brush = new LinearGradientBrush(ParseColor(theme.Accent), ParseColor(theme.Accent2), new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush CreateBrush(string hex, byte alpha = 255)
    {
        var color = ParseColor(hex);
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Цвет уровня тревожности в текущей теме (для кода, которому не доступны ресурсы WPF).</summary>
    public static string SeverityColor(Severity severity) => severity switch
    {
        Severity.Critical => Current.Hot,
        Severity.Elevated => Current.Warn,
        _ => Current.Accent,
    };

    public static void Apply(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var r = Application.Current.Resources;
        r["BgBrush"] = CreateBrush(theme.Background);
        r["SideBrush"] = CreateBrush(theme.Sidebar);
        r["CardBrush"] = CreateBrush(theme.Card);
        r["CardHoverBrush"] = CreateBrush(theme.CardHover);
        r["BorderBrush"] = CreateBrush(theme.Border);
        r["TextBrush"] = CreateBrush(theme.Text);
        r["SubTextBrush"] = CreateBrush(theme.SubText);
        r["AccentBrush"] = CreateBrush(theme.Accent);
        r["Accent2Brush"] = CreateBrush(theme.Accent2);
        r["AccentSoftBrush"] = CreateBrush(theme.Accent, 0x26);
        r["AccentHoverBrush"] = CreateBrush(theme.Accent, 0x70);
        r["TrackBrush"] = CreateBrush(theme.Track);
        r["ScrollBrush"] = CreateBrush(theme.SubText, 0x55);
        r["GoodBrush"] = CreateBrush(theme.Good);
        r["WarnBrush"] = CreateBrush(theme.Warn);
        r["HotBrush"] = CreateBrush(theme.Hot);
        r["WarnSoftBrush"] = CreateBrush(theme.Warn, 0x22);
        // Подложка выбранного сегмента или режима: нейтральная, акцентом выделяется только текст.
        r["SelectionBrush"] = CreateBrush(theme.Border);

        Current = theme;
        Changed?.Invoke(theme);
    }
}
