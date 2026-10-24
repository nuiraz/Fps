using System.Windows.Input;

namespace GameBooster.Services;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action execute, Func<bool>? can = null)
        : this(_ => execute(), can == null ? null : (_ => can()))
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? can = null)
    {
        _execute = execute;
        _can = can;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _can;
    private readonly Action<Exception>? _onError;
    private bool _busy;
    private event EventHandler? _changed;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? can = null, Action<Exception>? onError = null)
        : this(_ => execute(), can == null ? null : (_ => can()), onError)
    {
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? can = null, Action<Exception>? onError = null)
    {
        _execute = execute;
        _can = can;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
            CommandManager.RequerySuggested += value;
            _changed += value;
        }
        remove
        {
            CommandManager.RequerySuggested -= value;
            _changed -= value;
        }
    }

    public bool CanExecute(object? parameter) => !_busy && (_can?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (_busy)
            return;
        _busy = true;
        _changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            if (_onError != null)
                _onError(ex);
            else
                Faults.Report(ex, "Commande");
        }
        finally
        {
            _busy = false;
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
