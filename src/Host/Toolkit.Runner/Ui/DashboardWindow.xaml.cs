using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Toolkit.Common.Catalog;
using Toolkit.Common.Hosting;
using Toolkit.Common.Services;

namespace Toolkit.Runner.Ui;

public partial class DashboardWindow : Window
{
    private const string DefaultCatalogUrl =
        "https://raw.githubusercontent.com/C-McCarty/DesktopToolkit-catalog/main/catalog.json";

    private readonly ModuleSupervisor _supervisor;
    private readonly CatalogService _catalog = new();
    private List<ModuleViewModel> _moduleVms = new();
    private List<CatalogEntryViewModel> _catalogVms = new();
    private readonly DispatcherTimer _statusTimer;
    private bool _loading;

    public DashboardWindow(ModuleSupervisor supervisor)
    {
        InitializeComponent();
        _supervisor = supervisor;

        BuildModuleCards();

        _loading = true;
        StartWithWindowsCheck.IsChecked = StartupManager.IsEnabled();
        CatalogUrlBox.Text = string.IsNullOrWhiteSpace(_supervisor.Settings.Data.CatalogUrl)
            ? DefaultCatalogUrl
            : _supervisor.Settings.Data.CatalogUrl;
        _loading = false;

        Loaded += (_, _) => Diagnostics.Logger.Info("Dashboard rendered.");

        _supervisor.StateChanged += OnSupervisorStateChanged;
        _supervisor.ModulesChanged += OnModulesChanged;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _statusTimer.Tick += (_, _) => RefreshCards();
        _statusTimer.Start();

        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            _supervisor.StateChanged -= OnSupervisorStateChanged;
            _supervisor.ModulesChanged -= OnModulesChanged;
        };
    }

    // ---------------------------------------------------------------- installed tab

    private void BuildModuleCards()
    {
        _moduleVms = _supervisor.Modules.Select(m => new ModuleViewModel(m, _supervisor)).ToList();
        ModuleList.ItemsSource = _moduleVms;
        EmptyHint.Text = _moduleVms.Count == 0
            ? "No modules installed — add one from the Catalog tab."
            : $"{_moduleVms.Count} module(s) installed.";
        Diagnostics.Logger.Info($"Dashboard cards: {string.Join(", ", _moduleVms.Select(v => v.Name))}");
    }

    private void RefreshCards()
    {
        foreach (var vm in _moduleVms)
            vm.Refresh();
    }

    private void OnSupervisorStateChanged() => Dispatcher.Invoke(RefreshCards);

    private void OnModulesChanged() => Dispatcher.Invoke(() =>
    {
        BuildModuleCards();
        RefreshCatalogStates();
    });

    private void OpenModulesFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_supervisor.ModulesRoot);
            Process.Start("explorer.exe", _supervisor.ModulesRoot);
        }
        catch (Exception ex)
        {
            Diagnostics.Logger.Error("Open modules folder failed", ex);
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => _supervisor.Rescan();

    private void Kebab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void InstallFromZip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Install module from package",
            Filter = "Module package (*.zip)|*.zip|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        var result = _supervisor.InstallFromZip(dlg.FileName);
        if (result.Ok)
            MessageBox.Show(this, $"Installed {result.Manifest?.Name}.", "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(this, $"Install failed:\n{result.Error}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // ----------------------------------------------------------------- catalog tab

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        var url = CatalogUrlBox.Text.Trim();
        _supervisor.Settings.Data.CatalogUrl = url;
        _supervisor.Settings.Save();

        CatalogStatus.Text = "Loading catalog…";
        CatalogList.ItemsSource = null;
        try
        {
            var entries = await _catalog.FetchAsync(url);
            _catalogVms = entries
                .Select(en => new CatalogEntryViewModel(en, _supervisor, AfterCatalogInstallAsync))
                .ToList();
            CatalogList.ItemsSource = _catalogVms;
            CatalogStatus.Text = _catalogVms.Count == 0
                ? "Catalog is empty."
                : $"{_catalogVms.Count} module(s) available.";
        }
        catch (Exception ex)
        {
            Diagnostics.Logger.Error("Catalog fetch failed", ex);
            CatalogStatus.Text = $"Could not load catalog: {ex.Message}";
        }
    }

    private Task AfterCatalogInstallAsync()
    {
        // Supervisor.InstallFromCatalogAsync already rescanned (rebuilding the Installed tab);
        // just refresh the catalog rows' Install/Update/Installed states.
        RefreshCatalogStates();
        return Task.CompletedTask;
    }

    private void RefreshCatalogStates()
    {
        foreach (var vm in _catalogVms)
            vm.Refresh();
    }

    // ------------------------------------------------------------------- footer

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        StartupManager.SetEnabled(StartWithWindowsCheck.IsChecked == true, exePath);
        _supervisor.Settings.Data.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _supervisor.Settings.Save();
    }
}
