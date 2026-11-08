using System.Windows;
using System.Windows.Threading;
using PCMonitor.Core;
using PCMonitor.Diagnostics;
using PCMonitor.Hardware;
using PCMonitor.Platform;
using PCMonitor.Theming;
using PCMonitor.ViewModels;

namespace PCMonitor;

/// <summary>
/// Точка сборки приложения. Создаёт сервисы, связывает их и управляет жизненным циклом:
/// окно создаётся по требованию и уничтожается при закрытии, а датчики, кулеры и трей работают всегда.
/// </summary>
public partial class App : Application
{
    /// <summary>Через сколько после закрытия окна отдавать память системе (даём WPF доубрать за собой).</summary>
    private static readonly TimeSpan TrimDelayAfterClose = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan TrimDelayAfterTrayStart = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TrimInterval = TimeSpan.FromMinutes(10);

    private SingleInstance? _instance;
    private AppSettings? _settings;
    private TemperatureHistory? _history;
    private HardwareMonitor? _monitor;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private DispatcherTimer? _trimTimer;
    private bool _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        Log.Initialize(AppPaths.DataDirectory);
        RegisterCrashHandlers();

        var options = CommandLineOptions.Parse(e.Args);
        if (options.SetAutostart is { } enable)
        {
            Shutdown(Autostart.SetEnabled(enable) ? 0 : 1);
            return;
        }

        _instance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(ShowMainWindow));
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        Log.Info($"Запуск {typeof(App).Assembly.GetName().Version}, администратор: {SystemInfo.IsAdministrator}, PawnIO: {SystemInfo.IsPawnIoInstalled}");

        _settings = AppSettings.Load(AppPaths.Settings);
        ThemeManager.Apply();

        _history = new TemperatureHistory(AppPaths.History);
        _monitor = new HardwareMonitor(_settings);
        _viewModel = new MainViewModel(_settings, _history, _monitor.Wake);

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMainWindow;
        _tray.ExitRequested += ExitApplication;
        _tray.AllFansAutoRequested += _viewModel.Fans.ReleaseAll;
        _tray.DisplayPresetRequested += _viewModel.Display.ApplyPreset;

        _monitor.Updated += _history.Add; // в потоке датчиков, без перехода в UI
        _monitor.Updated += snapshot => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => OnSnapshot(snapshot));
        _monitor.Start();

        if (options.ScreenshotPrefix is { } prefix)
        {
            ShowMainWindow();
            _ = ScreenshotRunner.RunAsync(_window!, _viewModel, prefix).ContinueWith(_ => ExitApplication(), TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }

        if (options.StartInTray && _settings.StartMinimized)
        {
            ScheduleTrim(TrimDelayAfterTrayStart);
        }
        else
        {
            ShowMainWindow();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Выключение или выход из Windows: вернуть кулеры BIOS и сохранить всё.
        ExitApplication();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseResources();
        base.OnExit(e);
    }

    private void OnSnapshot(Snapshot snapshot)
    {
        if (_shuttingDown)
        {
            return;
        }

        if (_window is not null)
        {
            _viewModel!.Update(snapshot);
        }

        _tray!.Update(snapshot, _settings!.TrayTemp);
    }

    private void ShowMainWindow()
    {
        if (_shuttingDown || _viewModel is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new MainWindow(_viewModel);
            _window.Closed += OnMainWindowClosed;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _monitor!.IsUiVisible = true;
        if (_viewModel.CurrentPage == AppPage.Display)
        {
            // Окно закрыли на вкладке «Экран»: значения могли поменять кнопками на мониторе.
            _viewModel.Display.OnActivated();
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        _window = null;
        _monitor!.IsUiVisible = false;
        _viewModel!.History.IsActive = false;

        if (!_shuttingDown && !_settings!.CloseToTray)
        {
            ExitApplication();
            return;
        }

        // Окно уничтожено целиком — в фоне остаются только датчики и трей.
        ScheduleTrim(TrimDelayAfterClose);
    }

    /// <summary>Однократно через <paramref name="delay"/> и затем периодически отдаём память, пока окно закрыто.</summary>
    private void ScheduleTrim(TimeSpan delay)
    {
        var once = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };
        once.Tick += (_, _) =>
        {
            once.Stop();
            TrimIfHidden();
        };
        once.Start();

        if (_trimTimer is null)
        {
            _trimTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TrimInterval };
            _trimTimer.Tick += (_, _) => TrimIfHidden();
            _trimTimer.Start();
        }
    }

    private void TrimIfHidden()
    {
        if (_window is null)
        {
            NativeMethods.TrimWorkingSet();
        }
    }

    private void ExitApplication()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _window?.Close();
        Shutdown();
    }

    /// <summary>Порядок важен: сначала остановить датчики (они вернут кулеры в авто), затем сохранить данные.</summary>
    private void ReleaseResources()
    {
        _shuttingDown = true;
        _trimTimer?.Stop();
        _monitor?.Dispose();
        _monitor = null;
        _viewModel?.Dispose(); // гамма и затемнение — обратно в норму
        _history?.Flush();
        _settings?.SaveNow();
        _settings?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _instance?.Dispose();
        _instance = null;
        Log.Info("Выход");
    }

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Необработанное исключение в UI", e.Exception);
            // Ошибка в интерфейсе не должна оставлять кулеры в ручном режиме без присмотра.
            ReleaseResources();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Необработанное исключение", e.ExceptionObject as Exception);
            ReleaseResources();
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Warn("Незамеченное исключение в задаче", e.Exception);
    }
}
