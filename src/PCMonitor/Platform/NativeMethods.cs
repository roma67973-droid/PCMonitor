using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCMonitor.Platform;

public readonly record struct MemoryStatus(uint MemoryLoad, ulong TotalPhysical, ulong AvailablePhysical);

internal static partial class NativeMethods
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    public static MemoryStatus GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryStatus(status.dwMemoryLoad, status.ullTotalPhys, status.ullAvailPhys)
            : default;
    }

    /// <summary>
    /// Отдаёт системе неиспользуемую память процесса. Вызывается, когда окно закрыто и
    /// программа живёт только в трее, — в диспетчере задач она «худеет» до ~25 МБ.
    /// </summary>
    public static void TrimWorkingSet()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        _ = EmptyWorkingSet(process.Handle);
    }

    /// <summary>Скруглённые углы безрамочного окна в Windows 11 (на Windows 10 — молча ничего).</summary>
    public static void EnableRoundedCorners(IntPtr hwnd)
    {
        var preference = DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [LibraryImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr process);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Имена полей повторяют Win32 API.
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
