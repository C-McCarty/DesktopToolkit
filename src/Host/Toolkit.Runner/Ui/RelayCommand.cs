using System.Windows.Input;
using Toolkit.Runner.Diagnostics;

namespace Toolkit.Runner.Ui;

/// <summary>Minimal ICommand for binding buttons to view-model methods.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    private readonly string _name;

    public RelayCommand(Action execute, Func<bool>? canExecute = null, string? name = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _name = name ?? "command";
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        Logger.Info($"RelayCommand '{_name}' invoked.");
        _execute();
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
