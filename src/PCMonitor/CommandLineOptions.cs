namespace PCMonitor;

/// <param name="StartInTray">Запуск из автозагрузки (<c>--tray</c>).</param>
/// <param name="SetAutostart">Только включить/выключить автозапуск и выйти (<c>--autostart-on|off</c>).</param>
/// <param name="ScreenshotPrefix">Режим самопроверки: снять скриншоты всех страниц и выйти (<c>--screenshots &lt;prefix&gt;</c>).</param>
public sealed record CommandLineOptions(bool StartInTray, bool? SetAutostart, string? ScreenshotPrefix)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        bool? autostart = null;
        string? screenshots = null;
        var tray = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--tray":
                    tray = true;
                    break;
                case "--autostart-on":
                    autostart = true;
                    break;
                case "--autostart-off":
                    autostart = false;
                    break;
                case "--screenshots" when i + 1 < args.Count:
                    screenshots = args[++i];
                    break;
            }
        }

        return new CommandLineOptions(tray, autostart, screenshots);
    }
}
