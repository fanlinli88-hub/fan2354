using System.Windows.Input;

namespace WowVmMonitor.App.Mvvm;

public sealed class AsyncCommand : ObservableObject, ICommand, IDisposable
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private CancellationTokenSource? _cancellation;
    private bool _isExecuting;
    private Exception? _lastError;

    public AsyncCommand(
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetProperty(ref _isExecuting, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    public Exception? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public Task ExecutionTask { get; private set; } = Task.CompletedTask;

    public bool CanExecute(object? parameter) =>
        !IsExecuting && (_canExecute?.Invoke() ?? true);

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        ExecutionTask = ExecuteCoreAsync();
    }

    public void Cancel() => _cancellation?.Cancel();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }

    private async Task ExecuteCoreAsync()
    {
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        IsExecuting = true;
        LastError = null;
        try
        {
            await _execute(_cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LastError = exception;
        }
        finally
        {
            IsExecuting = false;
        }
    }
}
