using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PCMonitor.Platform;

/// <summary>
/// Программные фильтры поверх изображения, работают на любом мониторе, даже без DDC/CI:
/// затемнение (чёрное полупрозрачное окно, сквозь которое проходят клики) и тёплый оттенок
/// (гамма-таблица видеокарты). Создаётся и вызывается из UI-потока; <see cref="Dispose"/> — из любого.
/// </summary>
/// <remarks>
/// Гамму трогаем, только пока тёплый оттенок включён: до первой записи запоминаем исходную таблицу
/// (в ней может быть ICC-калибровка или f.lux), тепло накладываем поверх неё и потом возвращаем её же.
/// </remarks>
public sealed class ScreenFilters : IDisposable
{
    /// <summary>Потолок затемнения: экран никогда не становится полностью чёрным.</summary>
    private const double MaxDimOpacity = 0.85;

    /// <summary>
    /// Множители каналов при максимальном тепле. Сильнее Windows не даёт: без правки реестра
    /// SetDeviceGammaRamp отклоняет таблицы, слишком далёкие от линейной.
    /// </summary>
    private const double WarmGreen = 0.82;
    private const double WarmBlue = 0.62;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly List<DimOverlay> _overlays = [];
    private readonly Dictionary<string, ushort[]> _originalRamps = [];
    private readonly object _gammaGate = new();
    private int _dimPercent;
    private int _warmthPercent;
    private volatile bool _disposed;

    public ScreenFilters() => RebuildOverlays();

    /// <summary>Пересоздать окна затемнения и повторно применить тепло (после смены набора экранов).</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        RebuildOverlays();
        ApplyDim();
        _ = ReapplyWarmth();
    }

    /// <summary>
    /// Драйвер сбрасывает гамму после сна, экрана блокировки и выхода из полноэкранной игры —
    /// записываем тепло заново. Ничего не делает, если тепло выключено.
    /// </summary>
    public bool ReapplyWarmth() => _disposed || ApplyWarmth();

    /// <param name="percent">0 — выключено, 100 — максимум.</param>
    public void SetDim(int percent)
    {
        if (_disposed)
        {
            return;
        }

        _dimPercent = Math.Clamp(percent, 0, 100);
        ApplyDim();
    }

    /// <returns><c>false</c>, если основной экран отказался принять гамма-таблицу.</returns>
    public bool SetWarmth(int percent)
    {
        if (_disposed)
        {
            return true;
        }

        _warmthPercent = Math.Clamp(percent, 0, 100);
        return ApplyWarmth();
    }

    /// <summary>
    /// Вернуть экран в исходное состояние. Гамму можно восстанавливать из любого потока
    /// (так делает аварийный выход); окна — только из UI-потока, иначе они умрут вместе с процессом.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _warmthPercent = 0;
        _ = ApplyWarmth();
        if (_dispatcher.CheckAccess())
        {
            CloseOverlays();
        }
    }

    /// <param name="baseRamp">Исходная таблица экрана; <c>null</c> — линейная.</param>
    internal static ushort[] BuildGammaRamp(int warmthPercent, ushort[]? baseRamp = null)
    {
        var w = Math.Clamp(warmthPercent, 0, 100) / 100.0;
        var scale = new[] { 1.0, 1 - ((1 - WarmGreen) * w), 1 - ((1 - WarmBlue) * w) };
        var ramp = new ushort[NativeMethods.GammaRampLength * 3];
        for (var channel = 0; channel < 3; channel++)
        {
            for (var i = 0; i < NativeMethods.GammaRampLength; i++)
            {
                var index = (channel * NativeMethods.GammaRampLength) + i;
                var source = baseRamp is null ? i * 257 : baseRamp[index];
                ramp[index] = (ushort)Math.Round(source * scale[channel]);
            }
        }

        return ramp;
    }

    private void ApplyDim()
    {
        var opacity = _dimPercent / 100.0 * MaxDimOpacity;
        foreach (var overlay in _overlays)
        {
            overlay.Opacity = opacity;
            if (opacity > 0 && !overlay.Visible)
            {
                overlay.Show();
            }
            else if (opacity == 0 && overlay.Visible)
            {
                overlay.Hide();
            }
        }
    }

    private bool ApplyWarmth()
    {
        lock (_gammaGate)
        {
            var screens = Forms.Screen.AllScreens;
            if (_warmthPercent == 0)
            {
                // Тепло выключено: вернуть то, что было до нас, и больше гамму не трогать.
                foreach (var screen in screens)
                {
                    if (_originalRamps.Remove(screen.DeviceName, out var original))
                    {
                        _ = NativeMethods.SetGammaRamp(screen.DeviceName, original);
                    }
                }

                _originalRamps.Clear();
                return true;
            }

            var ok = true;
            foreach (var screen in screens)
            {
                if (!_originalRamps.TryGetValue(screen.DeviceName, out var original))
                {
                    original = NativeMethods.GetGammaRamp(screen.DeviceName);
                    if (original is null)
                    {
                        // Виртуальные дисплеи (Meta, SudoMaker и т.п.) гамму обычно не поддерживают — это не ошибка.
                        ok &= !screen.Primary;
                        continue;
                    }

                    _originalRamps[screen.DeviceName] = original;
                }

                ok &= NativeMethods.SetGammaRamp(screen.DeviceName, BuildGammaRamp(_warmthPercent, original)) || !screen.Primary;
            }

            return ok;
        }
    }

    private void RebuildOverlays()
    {
        CloseOverlays();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            _overlays.Add(new DimOverlay(screen.Bounds));
        }
    }

    private void CloseOverlays()
    {
        foreach (var overlay in _overlays)
        {
            overlay.Close();
            overlay.Dispose();
        }

        _overlays.Clear();
    }

    /// <summary>Чёрный слой поверх всех окон: не получает фокус и пропускает мышь насквозь.</summary>
    private sealed class DimOverlay : Forms.Form
    {
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;
        private const int WsExToolWindow = 0x80;
        private const int WsExTopmost = 0x8;
        private const int WsExNoActivate = 0x8000000;

        public DimOverlay(Drawing.Rectangle bounds)
        {
            FormBorderStyle = Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Drawing.Color.Black;
            StartPosition = Forms.FormStartPosition.Manual;
            Bounds = bounds;
            Opacity = 0;
        }

        protected override bool ShowWithoutActivation => true;

        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExTopmost | WsExNoActivate;
                return cp;
            }
        }
    }
}
