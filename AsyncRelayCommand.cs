// Required namespaces for asynchronous programming and command interface
using System;
using System.Threading.Tasks;
using System.Windows.Input;

// Summary:
// This class provides an implementation of the ICommand interface for asynchronous operations.
// It allows commands to be executed asynchronously and supports enabling/disabling the command
// based on a condition.
public class AsyncRelayCommand<T> : ICommand
{
    // Delegate for the asynchronous method to be executed
    private readonly Func<T, Task> _execute;

    // Delegate for the method that determines if the command can execute
    private readonly Func<T, bool> _canExecute;

    // Flag to indicate if the command is currently executing
    private bool _isExecuting;

    // Constructor to initialize the command with the execute and canExecute delegates
    public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool> canExecute = null)
    {
        // Ensure the execute delegate is not null
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));

        // Initialize the canExecute delegate, if provided
        _canExecute = canExecute;
    }

    // Event to handle changes in the command's ability to execute
    public event EventHandler CanExecuteChanged
    {
        // Subscribe to the CommandManager's RequerySuggested event
        add => CommandManager.RequerySuggested += value;

        // Unsubscribe from the CommandManager's RequerySuggested event
        remove => CommandManager.RequerySuggested -= value;
    }

    // Method to determine if the command can execute
    public bool CanExecute(object parameter)
    {
        // The command can execute if it is not currently executing and the canExecute delegate returns true
        return !_isExecuting && (_canExecute?.Invoke((T)parameter) ?? true);
    }

    // Asynchronous method to execute the command
    public async void Execute(object parameter)
    {
        // Check if the command can execute
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            // Set the executing flag to true and notify the CommandManager
            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();

            // Execute the asynchronous method
            await _execute((T)parameter);
        }
        finally
        {
            // Reset the executing flag and notify the CommandManager
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
