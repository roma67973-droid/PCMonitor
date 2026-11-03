using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using PCMonitor.Core;

namespace PCMonitor.Platform;

/// <summary>
/// Автозапуск через Планировщик задач. Задача стартует при входе в систему с наивысшими правами,
/// поэтому окно UAC не появляется (в отличие от ключа Run в реестре).
/// </summary>
public static class Autostart
{
    private const string TaskName = "PCMonitor";
    private const int SchtasksTimeoutMs = 10_000;

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <returns><c>true</c>, если состояние успешно изменено.</returns>
    public static bool SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            return RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;
        }

        var xmlPath = Path.Combine(AppPaths.DataDirectory, "autostart-task.xml");
        try
        {
            // schtasks принимает XML только в UTF-16.
            File.WriteAllText(xmlPath, BuildTaskXml(), Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F") == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Не удалось записать описание задачи автозапуска", e);
            return false;
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static string BuildTaskXml()
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PCMonitor.exe");
        using var identity = WindowsIdentity.GetCurrent();
        var user = SecurityElement.Escape(identity.Name);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>PC Monitor — мониторинг системы и управление кулерами</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT3S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>6</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>--tray</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(AppContext.BaseDirectory)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static int RunSchtasks(string arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return -1;
            }

            // Читаем вывод, иначе при заполнении буфера процесс зависнет.
            _ = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(SchtasksTimeoutMs))
            {
                Log.Warn($"schtasks {arguments} не завершился за {SchtasksTimeoutMs} мс");
                return -1;
            }

            if (process.ExitCode != 0 && !arguments.StartsWith("/Query", StringComparison.Ordinal))
            {
                Log.Warn($"schtasks {arguments} → {process.ExitCode}: {error.Trim()}");
            }

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Warn("Не удалось запустить schtasks.exe", e);
            return -1;
        }
    }
}
