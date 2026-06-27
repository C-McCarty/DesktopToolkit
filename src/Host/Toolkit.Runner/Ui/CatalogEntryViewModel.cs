using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Toolkit.Common.Catalog;
using Toolkit.Common.Hosting;
using Toolkit.Common.Manifest;

namespace Toolkit.Runner.Ui;

/// <summary>Binds one catalog entry to a row on the Catalog tab.</summary>
public sealed class CatalogEntryViewModel : INotifyPropertyChanged
{
    private readonly ModuleSupervisor _supervisor;
    private readonly Func<Task> _afterInstall;
    private bool _busy;

    public CatalogEntryViewModel(CatalogEntry entry, ModuleSupervisor supervisor, Func<Task> afterInstall)
    {
        Entry = entry;
        _supervisor = supervisor;
        _afterInstall = afterInstall;
        InstallCommand = new RelayCommand(Install, () => !_busy, name: $"Install[{entry.Id}]");
    }

    public CatalogEntry Entry { get; }

    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Version => $"v{Entry.Version}";

    private ModuleManifest? Installed => _supervisor.Modules.FirstOrDefault(m => m.Id == Entry.Id);

    public string ActionLabel
    {
        get
        {
            var inst = Installed;
            if (inst is null) return "Install";
            return IsNewer(Entry.Version, inst.Version) ? "Update" : "Reinstall";
        }
    }

    public string StatusText
    {
        get
        {
            var inst = Installed;
            if (inst is null) return "Not installed";
            return IsNewer(Entry.Version, inst.Version)
                ? $"Installed v{inst.Version} — update available"
                : $"Installed v{inst.Version}";
        }
    }

    public RelayCommand InstallCommand { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(StatusText));
    }

    private async void Install()
    {
        var verb = Installed is null ? "Install" : "Update";
        if (MessageBox.Show(
                $"{verb} {Name} {Version}?\n\nThis downloads and stores an executable from:\n{Entry.Package}\n\n"
                + "Only install from a source you trust.",
                $"{verb} module", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _busy = true;
        Diagnostics.Logger.Info($"Installing {Entry.Id} from {Entry.Package}");
        var result = await _supervisor.InstallFromCatalogAsync(Entry);
        _busy = false;

        if (result.Ok)
        {
            MessageBox.Show($"{Name} installed.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            await _afterInstall();
        }
        else
        {
            Diagnostics.Logger.Error($"Install failed for {Entry.Id}: {result.Error}");
            MessageBox.Show($"Install failed:\n{result.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsNewer(string candidate, string current) =>
        System.Version.TryParse(candidate, out var c) && System.Version.TryParse(current, out var cur) && c > cur;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
