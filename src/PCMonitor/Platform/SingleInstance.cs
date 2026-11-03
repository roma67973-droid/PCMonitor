namespace PCMonitor.Platform;

/// <summary>
/// Гарантирует одну копию программы. Повторный запуск не открывает вторую копию,
/// а просит уже работающую показать окно.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "PCMonitor.SingleInstance";
    private const string ActivateEventName = "PCMonitor.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activate;
    private readonly RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, Action onActivate)
    {
        _mutex = mutex;
        _activate = activate;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            activate, (_, _) => onActivate(), state: null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// Пытается стать единственной копией. Если копия уже есть — будит её и возвращает <c>null</c>.
    /// </summary>
    /// <param name="onActivate">Вызывается из пула потоков, когда кто-то запустил вторую копию.</param>
    public static SingleInstance? TryAcquire(Action onActivate)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
            {
                using (existing)
                {
                    existing.Set();
                }
            }

            return null;
        }

        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        return new SingleInstance(mutex, activate, onActivate);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activate?.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
