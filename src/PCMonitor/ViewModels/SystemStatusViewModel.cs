using System.Diagnostics;
using System.Windows.Input;
using PCMonitor.Core;
using PCMonitor.Hardware;
using PCMonitor.Platform;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>Общее состояние: статус системы, предупреждения о недоступных датчиках, сведения о самой программе.</summary>
public sealed class SystemStatusViewModel : ObservableObject
{
    private const int MemoryRefreshEveryTicks = 3;
    private const int GreetingRefreshEveryTicks = 60;

    private int _ticks;
    private bool _isLoading = true;
    private bool _showAdminBanner;
    private bool _showPawnIoBanner;
    private string _statusText = "Собираю данные…";
    private Severity _severity;
    private string _uptimeText = string.Empty;
    private string _appMemoryText = string.Empty;

    public SystemStatusViewModel()
    {
        OpenPawnIoPageCommand = new RelayCommand(OpenPawnIoPage);
    }

    public ICommand OpenPawnIoPageCommand { get; }

    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

    public bool ShowAdminBanner { get => _showAdminBanner; private set => SetProperty(ref _showAdminBanner, value); }

    public bool ShowPawnIoBanner { get => _showPawnIoBanner; private set => SetProperty(ref _showPawnIoBanner, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public Severity Severity { get => _severity; private set => SetProperty(ref _severity, value); }

    public string UptimeText { get => _uptimeText; private set => SetProperty(ref _uptimeText, value); }

    public string AppMemoryText { get => _appMemoryText; private set => SetProperty(ref _appMemoryText, value); }

    public string Greeting => DateTime.Now.Hour switch
    {
        >= 5 and < 12 => "Доброе утро",
        >= 12 and < 18 => "Добрый день",
        >= 18 and < 23 => "Добрый вечер",
        _ => "Доброй ночи",
    };

    public string AdminStatus => SystemInfo.IsAdministrator ? "есть" : "нет";

    public string PawnIoStatus => SystemInfo.IsPawnIoInstalled ? "установлен" : "не установлен";

    public void Update(Snapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);

        IsLoading = false;
        _ticks++;

        ShowAdminBanner = !SystemInfo.IsAdministrator;
        ShowPawnIoBanner = !ShowAdminBanner && !SystemInfo.IsPawnIoInstalled && (s.CpuTemp is null || !s.HasMotherboardSensors);

        var ramPressure = s.RamLoad >= Thermals.RamPressurePercent;
        Severity = new[]
        {
            Thermals.Cpu.Classify(s.CpuTemp),
            Thermals.Gpu.Classify(s.GpuTemp),
            ramPressure ? Severity.Elevated : Severity.Normal,
        }.Max();
        StatusText = Severity switch
        {
            Severity.Critical => "Перегрев! Проверьте охлаждение",
            Severity.Elevated when ramPressure => "Оперативная память почти заполнена",
            Severity.Elevated => "Система под нагрузкой, температура повышена",
            _ => "Всё в норме",
        };

        UptimeText = Units.Uptime(TimeSpan.FromMilliseconds(Environment.TickCount64));

        if (_ticks % MemoryRefreshEveryTicks == 1)
        {
            AppMemoryText = $"{Environment.WorkingSet / (1024.0 * 1024):0} МБ ОЗУ";
        }

        if (_ticks % GreetingRefreshEveryTicks == 1)
        {
            OnPropertyChanged(nameof(Greeting));
        }
    }

    private static void OpenPawnIoPage()
    {
        // Через explorer.exe — браузер откроется с правами пользователя, а не администратора.
        try
        {
            Process.Start("explorer.exe", SystemInfo.PawnIoDownloadPage.AbsoluteUri)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Warn("Не удалось открыть страницу PawnIO", e);
        }
    }
}
