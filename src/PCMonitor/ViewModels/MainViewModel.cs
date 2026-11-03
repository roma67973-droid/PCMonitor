using PCMonitor.Core;
using PCMonitor.Hardware;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

public enum AppPage
{
    Overview,
    Fans,
    Display,
    History,
    Themes,
    Settings,
}

/// <summary>Корневая модель окна: навигация и раздача снимков датчиков по страницам.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private AppPage _currentPage = AppPage.Overview;

    public MainViewModel(AppSettings settings, TemperatureHistory history, Action requestRefresh)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AppSettings = settings;
        Status = new SystemStatusViewModel();
        Fans = new FansViewModel(settings, Status, requestRefresh);
        Overview = new OverviewViewModel(Status, Fans, new RelayCommand(() => CurrentPage = AppPage.Fans));
        Display = new DisplayViewModel(settings);
        History = new HistoryViewModel(history);
        Themes = new ThemesViewModel(settings);
        Settings = new SettingsViewModel(settings, Status, requestRefresh);
    }

    public AppSettings AppSettings { get; }

    public SystemStatusViewModel Status { get; }

    public OverviewViewModel Overview { get; }

    public FansViewModel Fans { get; }

    public DisplayViewModel Display { get; }

    public HistoryViewModel History { get; }

    public ThemesViewModel Themes { get; }

    public SettingsViewModel Settings { get; }

    public AppPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(PageTitle));
                History.IsActive = value == AppPage.History;
                if (value == AppPage.Display)
                {
                    Display.OnActivated();
                }
            }
        }
    }

    public string PageTitle => CurrentPage switch
    {
        AppPage.Fans => "Кулеры",
        AppPage.Display => "Экран",
        AppPage.History => "История температур",
        AppPage.Themes => "Темы оформления",
        AppPage.Settings => "Настройки",
        _ => "Обзор системы",
    };

    /// <summary>Вызывается в UI-потоке на каждый снимок датчиков, пока окно открыто.</summary>
    public void Update(Snapshot snapshot)
    {
        Status.Update(snapshot);
        Overview.Update(snapshot);
        Fans.Update(snapshot);
        History.OnTick();
    }

    /// <summary>Вернуть экрану исходную гамму и убрать затемнение.</summary>
    public void Dispose() => Display.Dispose();
}
