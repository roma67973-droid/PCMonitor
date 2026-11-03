using PCMonitor.Core;
using PCMonitor.Platform;
using PCMonitor.ViewModels.Infrastructure;

namespace PCMonitor.ViewModels;

/// <summary>Страница «Настройки». Каждое изменение сразу сохраняется.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _requestRefresh;
    private bool _autostartEnabled;

    public SettingsViewModel(AppSettings settings, SystemStatusViewModel status, Action requestRefresh)
    {
        _settings = settings;
        _requestRefresh = requestRefresh;
        Status = status;
        _autostartEnabled = Autostart.IsEnabled();
    }

    public SystemStatusViewModel Status { get; }

    public string SettingsPath => _settings.FilePath;

    public bool AutostartEnabled
    {
        get => _autostartEnabled;
        set
        {
            Autostart.SetEnabled(value);
            // Перечитываем фактическое состояние: schtasks мог отказать.
            _autostartEnabled = Autostart.IsEnabled();
            OnPropertyChanged();
        }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set => Update(() => _settings.StartMinimized = value);
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set => Update(() => _settings.CloseToTray = value);
    }

    public bool TrayTemp
    {
        get => _settings.TrayTemp;
        set => Update(() => _settings.TrayTemp = value);
    }

    public bool FailSafe
    {
        get => _settings.FailSafe;
        set => Update(() => _settings.FailSafe = value);
    }

    public bool Animations
    {
        get => _settings.Animations;
        set => Update(() => _settings.Animations = value);
    }

    public bool HideEmptyFans
    {
        get => _settings.HideEmptyFans;
        set => Update(() => _settings.HideEmptyFans = value, refresh: true);
    }

    public int IntervalMs
    {
        get => _settings.IntervalMs;
        set => Update(() => _settings.IntervalMs = value, refresh: true);
    }

    public string FailSafeDescription =>
        $"Если CPU ≥ {Thermals.EmergencyCpu:0}°C или GPU ≥ {Thermals.EmergencyGpu:0}°C — все управляемые кулеры на 100%. "
        + "При выходе из программы кулеры возвращаются под управление BIOS.";

    private void Update(Action apply, bool refresh = false, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        apply();
        _settings.SaveSoon();
        OnPropertyChanged(propertyName);
        if (refresh)
        {
            _requestRefresh();
        }
    }
}
