using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PCMonitor.Controls;

/// <summary>
/// Подложка выбранного пункта, которая переезжает к новому выбору, а не перескакивает.
/// Оборачивает панель с RadioButton (сегменты, меню) и рисует под ними одну плашку
/// (идея animated-background из motion-primitives, MIT). Сами пункты при выборе фон не рисуют.
/// </summary>
public sealed class SlidingHighlight : Decorator
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SlidingHighlight), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(SlidingHighlight), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Отступ плашки внутрь от границ пункта (у пунктов меню поля заложены в шаблон).</summary>
    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
        nameof(Inset), typeof(Thickness), typeof(SlidingHighlight), new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Необязательная полоска у левого края плашки (как в боковом меню).</summary>
    public static readonly DependencyProperty IndicatorProperty = DependencyProperty.Register(
        nameof(Indicator), typeof(Brush), typeof(SlidingHighlight), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty HighlightRectProperty = DependencyProperty.Register(
        "HighlightRect", typeof(Rect), typeof(SlidingHighlight), new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double IndicatorWidth = 3;
    private const double IndicatorHeight = 18;
    private static readonly Duration SlideDuration = TimeSpan.FromMilliseconds(260);

    private ToggleButton? _selected;
    private Rect _target = Rect.Empty;

    public SlidingHighlight()
    {
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnChecked));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnUnchecked));

        // Страницы скрываются, а не выгружаются: выбор мог поменяться, пока нас не было видно.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _target = Rect.Empty;
                InvalidateArrange();
            }
        };
    }

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public Thickness Inset { get => (Thickness)GetValue(InsetProperty); set => SetValue(InsetProperty, value); }

    public Brush? Indicator { get => (Brush?)GetValue(IndicatorProperty); set => SetValue(IndicatorProperty, value); }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);

        // Размер окна или пунктов поменялся — плашка встаёт на место сразу, без анимации.
        if (_selected is null || _selected.IsChecked != true || !IsAncestorOf(_selected))
        {
            _selected = FindChecked(Child);
        }

        MoveTo(_selected, animate: false);
        return size;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        var rect = (Rect)GetValue(HighlightRectProperty);
        if (rect.IsEmpty || rect.Width <= 0)
        {
            return;
        }

        if (Fill is { } fill)
        {
            drawingContext.DrawRoundedRectangle(fill, null, rect, CornerRadius, CornerRadius);
        }

        if (Indicator is { } indicator)
        {
            var bar = new Rect(rect.X, rect.Y + ((rect.Height - IndicatorHeight) / 2), IndicatorWidth, IndicatorHeight);
            drawingContext.DrawRoundedRectangle(indicator, null, bar, 1.5, 1.5);
        }
    }

    private static ToggleButton? FindChecked(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ToggleButton { IsChecked: true } toggle)
        {
            return toggle;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindChecked(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnChecked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is ToggleButton toggle)
        {
            _selected = toggle;
            MoveTo(toggle, animate: IsLoaded && Motion.GetIsEnabled(this));
        }
    }

    /// <summary>Выбор сняли совсем (а не перенесли на соседний пункт) — плашку убираем.</summary>
    private void OnUnchecked(object sender, RoutedEventArgs e)
    {
        // RadioButton сначала снимает выбор с соседей и только потом сообщает о своём —
        // проверяем позже, чтобы не потерять стартовую точку анимации.
        if (e.OriginalSource is ToggleButton toggle && ReferenceEquals(toggle, _selected))
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (ReferenceEquals(toggle, _selected) && toggle.IsChecked != true)
                {
                    _selected = null;
                    MoveTo(null, animate: false);
                }
            });
        }
    }

    private void MoveTo(ToggleButton? item, bool animate)
    {
        if (item is not null && !item.IsVisible)
        {
            // Пока скрыто, мерить нечего; место плашки найдётся при следующем показе.
            return;
        }

        var target = Rect.Empty;
        if (item is not null && IsAncestorOf(item))
        {
            var bounds = item.TransformToAncestor(this).TransformBounds(new Rect(item.RenderSize));
            var inset = Inset;
            target = new Rect(
                bounds.X + inset.Left,
                bounds.Y + inset.Top,
                Math.Max(0, bounds.Width - inset.Left - inset.Right),
                Math.Max(0, bounds.Height - inset.Top - inset.Bottom));
        }

        if (target == _target)
        {
            return;
        }

        var from = (Rect)GetValue(HighlightRectProperty);
        _target = target;
        if (!animate || from.IsEmpty || target.IsEmpty)
        {
            BeginAnimation(HighlightRectProperty, null);
            SetValue(HighlightRectProperty, target);
            return;
        }

        var slide = new RectAnimation(from, target, SlideDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(HighlightRectProperty, slide);
    }
}
