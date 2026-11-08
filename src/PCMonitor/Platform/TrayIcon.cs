using System.Windows;
using PCMonitor.Core;
using PCMonitor.Hardware;
using PCMonitor.Theming;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace PCMonitor.Platform;

/// <summary>
/// Значок в трее: температура CPU прямо на иконке, подсказка с нагрузкой и быстрое меню.
/// Иконка перерисовывается только когда меняется температура или тема.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    /// <summary>Лимит Windows на длину подсказки NotifyIcon.</summary>
    private const int MaxTooltipLength = 127;

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _appIcon;
    private IntPtr _renderedIcon;
    private int? _renderedTemperature;

    public TrayIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))
            ?? throw new InvalidOperationException("Не найден ресурс Assets/app.ico");
        using (resource.Stream)
        {
            _appIcon = new Drawing.Icon(resource.Stream, Forms.SystemInformation.SmallIconSize);
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "PC Monitor",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
    }

    public event Action? OpenRequested;

    public event Action? ExitRequested;

    public event Action? AllFansAutoRequested;

    public event Action<DisplayPreset>? DisplayPresetRequested;

    public void Update(Snapshot snapshot, bool showTemperature)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _notifyIcon.Text = BuildTooltip(snapshot);

        int? temperature = showTemperature && snapshot.CpuTemp is { } t ? (int)Math.Round(t) : null;
        if (temperature == _renderedTemperature)
        {
            return;
        }

        _renderedTemperature = temperature;

        if (temperature is not { } value)
        {
            _notifyIcon.Icon = _appIcon;
            ReleaseRenderedIcon();
            return;
        }

        var handle = RenderTemperatureIcon(value);
        _notifyIcon.Icon = Drawing.Icon.FromHandle(handle);
        ReleaseRenderedIcon();
        _renderedIcon = handle;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _appIcon.Dispose();
        ReleaseRenderedIcon();
    }

    private static string BuildTooltip(Snapshot s)
    {
        var tooltip = $"CPU {Units.Percent(s.CpuLoad)}  {Units.Temperature(s.CpuTemp)}";
        if (s.HasGpu)
        {
            tooltip += $"\nGPU {Units.Percent(s.GpuLoad)}  {Units.Temperature(s.GpuTemp)}";
        }

        tooltip += $"\nОЗУ {Units.Percent(s.RamLoad)}  ({s.RamUsedGb:0.0} / {s.RamTotalGb:0} ГБ)";
        return tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
    }

    /// <summary>Рисует скруглённый квадрат цвета уровня тревожности с числом температуры.</summary>
    private static IntPtr RenderTemperatureIcon(int celsius)
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        using var bitmap = new Drawing.Bitmap(size, size);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            var color = ThemeManager.ParseColor(ThemeManager.SeverityColor(Thermals.Cpu.Classify(celsius)));
            using var shape = RoundedRectangle(new Drawing.RectangleF(0, 0, size - 0.5f, size - 0.5f), size * 0.25f);
            using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(color.R, color.G, color.B));
            g.FillPath(fill, shape);

            var text = celsius.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var fontSize = text.Length >= 3 ? size * 0.5f : size * 0.7f;
            using var font = new Drawing.Font("Segoe UI", fontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var format = new Drawing.StringFormat
            {
                Alignment = Drawing.StringAlignment.Center,
                LineAlignment = Drawing.StringAlignment.Center,
            };

            // Тёмный текст на светлом фоне (жёлтый «тепло»), белый — на остальных.
            var luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
            using var textBrush = new Drawing.SolidBrush(luminance > 170 ? Drawing.Color.FromArgb(20, 22, 30) : Drawing.Color.White);
            g.DrawString(text, font, textBrush, new Drawing.RectangleF(-1, 0.5f, size + 2, size), format);
        }

        return bitmap.GetHicon();
    }

    private static Drawing2D.GraphicsPath RoundedRectangle(Drawing.RectangleF r, float radius)
    {
        var path = new Drawing2D.GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()),
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9.5f),
            ForeColor = Drawing.Color.FromArgb(235, 238, 248),
        };
        menu.Items.Add("Открыть PC Monitor", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Все кулеры — авто", null, (_, _) => AllFansAutoRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Экран: ночь", null, (_, _) => DisplayPresetRequested?.Invoke(DisplayPreset.Night));
        menu.Items.Add("Экран: вечер", null, (_, _) => DisplayPresetRequested?.Invoke(DisplayPreset.Evening));
        menu.Items.Add("Экран: день", null, (_, _) => DisplayPresetRequested?.Invoke(DisplayPreset.Day));
        menu.Items.Add("Экран: максимум", null, (_, _) => DisplayPresetRequested?.Invoke(DisplayPreset.Max));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke());
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.Padding = new Forms.Padding(4, 5, 4, 5);
        }

        return menu;
    }

    private void ReleaseRenderedIcon()
    {
        if (_renderedIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_renderedIcon);
            _renderedIcon = IntPtr.Zero;
        }
    }

    private sealed class DarkMenuColors : Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Background = Drawing.Color.FromArgb(24, 28, 44);
        private static readonly Drawing.Color Highlight = Drawing.Color.FromArgb(40, 48, 76);
        private static readonly Drawing.Color Edge = Drawing.Color.FromArgb(48, 56, 86);

        public override Drawing.Color ToolStripDropDownBackground => Background;

        public override Drawing.Color MenuBorder => Edge;

        public override Drawing.Color MenuItemBorder => Highlight;

        public override Drawing.Color MenuItemSelected => Highlight;

        public override Drawing.Color MenuItemSelectedGradientBegin => Highlight;

        public override Drawing.Color MenuItemSelectedGradientEnd => Highlight;

        public override Drawing.Color ImageMarginGradientBegin => Background;

        public override Drawing.Color ImageMarginGradientMiddle => Background;

        public override Drawing.Color ImageMarginGradientEnd => Background;

        public override Drawing.Color SeparatorDark => Edge;

        public override Drawing.Color SeparatorLight => Background;
    }
}
