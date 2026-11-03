using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PCMonitor.ViewModels;

namespace PCMonitor.Diagnostics;

/// <summary>
/// Инструмент разработчика (<c>--screenshots &lt;prefix&gt;</c>): ждёт первых данных датчиков,
/// снимает каждую страницу в PNG и записывает потребление памяти. Используется для проверки вёрстки.
/// </summary>
internal static class ScreenshotRunner
{
    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan PageSettle = TimeSpan.FromSeconds(1.5);

    public static async Task RunAsync(Window window, MainViewModel viewModel, string prefix)
    {
        await Task.Delay(WarmUp).ConfigureAwait(true);

        foreach (var page in Enum.GetValues<AppPage>())
        {
            viewModel.CurrentPage = page;
            await Task.Delay(PageSettle).ConfigureAwait(true);
            window.UpdateLayout();
            Capture((FrameworkElement)window.Content, $"{prefix}{(int)page}.png");
        }

        await File.WriteAllTextAsync($"{prefix}mem.txt", $"{Environment.WorkingSet / (1024 * 1024)} MB").ConfigureAwait(true);
    }

    private static void Capture(FrameworkElement root, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap(
            (int)(root.ActualWidth * dpi.DpiScaleX),
            (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
