using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Toolkit.Common.Hosting;
using Toolkit.Common.Manifest;

namespace Toolkit.Runner.Ui;

/// <summary>Binds one discovered module to a dashboard card.</summary>
public sealed class ModuleViewModel : INotifyPropertyChanged
{
    private readonly ModuleSupervisor _supervisor;

    public ModuleViewModel(ModuleManifest manifest, ModuleSupervisor supervisor)
    {
        Manifest = manifest;
        _supervisor = supervisor;

        ConfigureCommand = new RelayCommand(OpenSettings, name: $"Configure[{manifest.Id}]");
        IdentifyCommand = new RelayCommand(async () => await _supervisor.IdentifyAsync(Manifest), name: $"Identify[{manifest.Id}]");
        ResetCommand = new RelayCommand(ResetSettings, name: $"Reset[{manifest.Id}]");
        RemoveCommand = new RelayCommand(Remove, name: $"Remove[{manifest.Id}]");
    }

    public ModuleManifest Manifest { get; }

    public string Name => Manifest.Name;
    public string Description => Manifest.Description;
    public string Version => $"v{Manifest.Version}";
    public string KindLabel => Manifest.Kind == ModuleKind.Background ? "Background" : "App";

    /// <summary>Only background modules have an on/off state; window modules run ad-hoc.</summary>
    public bool CanEnable => Manifest.Kind == ModuleKind.Background;

    public bool IsEnabled
    {
        get => _supervisor.IsEnabled(Manifest);
        set
        {
            if (value == _supervisor.IsEnabled(Manifest))
                return;
            Diagnostics.Logger.Info($"Enable toggle [{Manifest.Id}] -> {value}");
            _supervisor.SetEnabled(Manifest, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText
    {
        get
        {
            if (Manifest.Kind == ModuleKind.Background)
                return _supervisor.IsRunning(Manifest) ? "Running" : (IsEnabled ? "Enabled" : "Disabled");
            return _supervisor.IsRunning(Manifest) ? "Open" : "Ready";
        }
    }

    public RelayCommand ConfigureCommand { get; }
    public RelayCommand IdentifyCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(StatusText));
    }

    private void ResetSettings()
    {
        if (MessageBox.Show(
                $"Reset {Name} to its default settings?",
                "Reset module", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _supervisor.ResetModule(Manifest);
        Refresh();
    }

    private void Remove()
    {
        if (MessageBox.Show(
                $"Remove {Name}? This deletes its files from the modules folder. Your other tools are unaffected.",
                "Remove module", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _supervisor.RemoveModule(Manifest); // raises ModulesChanged -> dashboard rebuilds
    }

    private async void OpenSettings()
    {
        // Modules with a richer config screen host their own window; the host just
        // asks them to show it. Everything else gets the generic schema dialog.
        if (Manifest.SettingsWindow)
        {
            await _supervisor.ShowConfigAsync(Manifest);
            Refresh();
            return;
        }

        var window = new SettingsWindow(Manifest, _supervisor) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
