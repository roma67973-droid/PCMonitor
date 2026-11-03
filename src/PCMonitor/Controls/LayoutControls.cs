using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCMonitor.Controls;

// Простые «шаблонные» элементы: логика нулевая, внешний вид — в Resources/Styles.xaml.
// Нужны, чтобы не копировать одинаковые куски разметки по страницам.

/// <summary>Строка настройки: заголовок, пояснение и элемент управления справа (содержимое).</summary>
public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));

    static SettingRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(typeof(SettingRow)));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}

/// <summary>Плитка показателя: подпись сверху, значение крупно.</summary>
public sealed class StatTile : Control
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueBrushProperty =
        DependencyProperty.Register(nameof(ValueBrush), typeof(Brush), typeof(StatTile), new PropertyMetadata(null));

    static StatTile() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StatTile), new FrameworkPropertyMetadata(typeof(StatTile)));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Цвет значения; если не задан — обычный цвет текста.</summary>
    public Brush? ValueBrush { get => (Brush?)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
}

/// <summary>Заголовок карточки: цветной бейдж и название; содержимое (например, ссылка) — справа.</summary>
public sealed class CardHeader : ContentControl
{
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(nameof(Badge), typeof(string), typeof(CardHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(CardHeader), new PropertyMetadata(string.Empty));

    static CardHeader() => DefaultStyleKeyProperty.OverrideMetadata(typeof(CardHeader), new FrameworkPropertyMetadata(typeof(CardHeader)));

    public string Badge { get => (string)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
}
