using System.Windows.Input;

namespace PCMonitor.ViewModels.Infrastructure;

/// <summary>Команда-обёртка над делегатом. Всегда доступна — блокировки кнопок в программе не нужны.</summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    // Интерфейс требует событие, но доступность команды никогда не меняется.
    event EventHandler? ICommand.CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}

/// <summary>Команда с типизированным параметром из <c>CommandParameter</c>.</summary>
public sealed class RelayCommand<T>(Action<T> execute) : ICommand
{
    event EventHandler? ICommand.CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is T;

    public void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            execute(value);
        }
    }
}
