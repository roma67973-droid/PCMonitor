using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PCMonitor.Controls;
using PCMonitor.Platform;
using PCMonitor.ViewModels;

namespace PCMonitor;

/// <summary>
/// Оболочка: безрамочное окно, навигация, анимация смены страниц.
/// Логики приложения здесь нет — только поведение окна.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Без рамки развёрнутое окно выходит за край экрана на ширину невидимой рамки.</summary>
    private static readonly Thickness MaximizedInset = new(7);

    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        var settings = viewModel.AppSettings;
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);

        SourceInitialized += (_, _) => NativeMethods.EnableRoundedCorners(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) => OnStateChanged();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowPage(_viewModel.CurrentPage, animate: false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            var settings = _viewModel.AppSettings;
            settings.WindowWidth = ActualWidth;
            settings.WindowHeight = ActualHeight;
            settings.SaveSoon();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Модель живёт дольше окна — отписываемся, чтобы окно не держалось в памяти.
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            ShowPage(_viewModel.CurrentPage, animate: true);
        }
    }

    private void ShowPage(AppPage page, bool animate)
    {
        FrameworkElement? shown = null;
        foreach (FrameworkElement element in Pages.Children)
        {
            var isCurrent = element.Tag is AppPage p && p == page;
            element.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
            if (isCurrent)
            {
                shown = element;
            }
        }

        if (shown is not null && animate && Motion.GetIsEnabled(this))
        {
            AnimateIn(shown);
        }
    }

    /// <summary>Лёгкое появление: прозрачность 0→1 и сдвиг на 10 px снизу.</summary>
    private static void AnimateIn(FrameworkElement page)
    {
        var shift = new TranslateTransform(0, 10);
        page.RenderTransform = shift;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        page.BeginAnimation(OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void OnStateChanged()
    {
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? MaximizedInset : default;
        MaximizeButton.Content = maximized ? RestoreGlyph : MaximizeGlyph;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
