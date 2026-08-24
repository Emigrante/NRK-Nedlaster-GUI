using System;
using System.Threading.Tasks;
using System.Windows.Input;
using NRKLastNed.Core.Models;
using NRKLastNed.Core.Services;

namespace NRKLastNed.Core.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private readonly Action<Exception>? _onException;
        private readonly bool _allowConcurrentExecution;
        private bool _isExecuting;

        public AsyncRelayCommand(
            Func<object?, Task> execute,
            Predicate<object?>? canExecute = null,
            Action<Exception>? onException = null,
            bool allowConcurrentExecution = true)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _onException = onException;
            _allowConcurrentExecution = allowConcurrentExecution;
        }

        public AsyncRelayCommand(
            Func<Task> execute,
            Func<bool>? canExecute = null,
            Action<Exception>? onException = null,
            bool allowConcurrentExecution = true)
            : this(_ => execute(), canExecute == null ? null : _ => canExecute(), onException, allowConcurrentExecution)
        {
        }

        public bool CanExecute(object? parameter)
        {
            if (!_allowConcurrentExecution && _isExecuting)
                return false;

            return _canExecute == null || _canExecute(parameter);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();

            try
            {
                await _execute(parameter);
            }
            catch (OperationCanceledException)
            {
                // Ignorer forventede avbrudd
            }
            catch (Exception ex)
            {
                LogService.Log($"Uventet feil i asynkron kommando: {ex.Message}", LogLevel.Error);
                _onException?.Invoke(ex);
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public static class CommandManager
    {
        public static event EventHandler? RequerySuggested;

        public static void InvalidateRequerySuggested()
        {
            RequerySuggested?.Invoke(null, EventArgs.Empty);
        }
    }
}
