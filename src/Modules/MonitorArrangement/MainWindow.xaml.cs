using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using MonitorArrangement.Interop;
using MonitorArrangement.Models;
using MonitorArrangement.Services;
using Toolkit.Common.Services;
using Toolkit.Common.Settings;

namespace MonitorArrangement;

public partial class MainWindow : Window
{
    private const string ModuleId = "monitor-arrangement";

    private readonly SettingsStore _settings = new();
    private readonly ObservableCollection<MonitorRow> _rows = new();
    private readonly List<List<(string dev, int x, int y)>> _undo = new();
    private readonly List<List<(string dev, int x, int y)>> _redo = new();

    private List<MonitorInfo> _monitors = new();
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();

        _settings.Load();
        RowsList.ItemsSource = _rows;
        Canvas.DragStarted += PushUndo;

        LoadCurrent();
        ApplyStoredSettingsToUi();
        UpdateUndoButtons();

        _loaded = true;
    }

    // ------------------------------------------------------------- load / model

    private void LoadCurrent()
    {
        try
        {
            _monitors = MonitorService.GetMonitors();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to read monitor config:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _rows.Clear();
        for (int i = 0; i < _monitors.Count; i++)
        {
            var row = new MonitorRow(_monitors[i], i);
            row.PositionEdited += () => Canvas.Redraw();
            _rows.Add(row);
        }
        Canvas.LoadMonitors(_rows.ToList());
    }

    // ------------------------------------------------------------- undo / redo

    private List<(string dev, int x, int y)> Snapshot() =>
        _monitors.Select(m => (m.DeviceName, m.X, m.Y)).ToList();

    private void PushUndo()
    {
        _undo.Add(Snapshot());
        if (_undo.Count > AppConstants.UndoMax)
            _undo.RemoveAt(0);
        _redo.Clear();
        UpdateUndoButtons();
    }

    private void RestoreSnapshot(List<(string dev, int x, int y)> snap)
    {
        var byDevice = snap.ToDictionary(s => s.dev, s => (s.x, s.y));
        foreach (var m in _monitors)
        {
            if (byDevice.TryGetValue(m.DeviceName, out var p))
            {
                m.X = p.Item1;
                m.Y = p.Item2;
            }
        }
        foreach (var r in _rows)
            r.NotifyMovedFromCanvas();
        Canvas.Refresh();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        _redo.Add(Snapshot());
        var snap = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreSnapshot(snap);
        UpdateUndoButtons();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        _undo.Add(Snapshot());
        var snap = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RestoreSnapshot(snap);
        UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        UndoBtn.IsEnabled = _undo.Count > 0;
        RedoBtn.IsEnabled = _redo.Count > 0;
    }

    // ----------------------------------------------------------------- actions

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var stranded = MonitorService.FindStranded(_monitors);
        if (stranded.Count > 0)
        {
            var names = string.Join(", ", stranded.Select(m => $"{m.Index} ({m.FriendlyName})"));
            var go = MessageBox.Show(this,
                $"These monitors have a gap on all sides and don't touch another monitor:\n\n{names}\n\n"
                + "Windows requires a contiguous layout and will likely reject this arrangement. Apply anyway?",
                "Gap detected", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (go != MessageBoxResult.Yes) return;
        }

        if (MessageBox.Show(this, "Apply the new monitor arrangement?\n\nThe screen may flicker briefly.",
                "Apply", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        PushUndo();
        var (ok, err) = MonitorService.ApplyLayout(_monitors);
        if (ok)
        {
            MessageBox.Show(this, "Monitor arrangement applied.", "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);
            LoadCurrent();
        }
        else
        {
            MessageBox.Show(this, $"Apply failed:\n{err}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Identify_Click(object sender, RoutedEventArgs e) => IdentifyMonitors();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Reload current Windows arrangement?", "Reset",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        PushUndo();
        LoadCurrent();
    }

    private void CopyDiag_Click(object sender, RoutedEventArgs e)
    {
        var lines = new List<string> { "Monitor Arrangement — diagnostics" };
        var positions = new Dictionary<(int, int, int, int), List<int>>();
        foreach (var m in _monitors)
        {
            var key = (m.X, m.Y, m.Width, m.Height);
            if (!positions.TryGetValue(key, out var list)) positions[key] = list = new List<int>();
            list.Add(m.Index);
            lines.Add($"  [{m.Index}] {m.FriendlyName} ({m.DeviceName})\n"
                      + $"        pos=({m.X},{m.Y})  size={m.Width}x{m.Height}  "
                      + $"{m.RefreshRate}Hz  {(m.IsPrimary ? "PRIMARY" : "secondary")}");
        }

        var mirrored = positions.Values.Where(idxs => idxs.Count > 1).ToList();
        if (mirrored.Count > 0)
        {
            var groups = string.Join("; ", mirrored.Select(g => string.Join("+", g)));
            lines.Add($"  ** MIRRORED/DUPLICATE groups: {groups} **");
        }

        try { Clipboard.SetText(string.Join("\n", lines)); } catch { /* clipboard busy */ }
        MessageBox.Show(this, "Monitor diagnostics copied to clipboard.\n\nPaste it into a message to share.",
            "Diagnostics copied", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void IdentifyMonitors() => IdentifyOverlay.Show(_monitors);

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            DefaultExt = ".json",
            Filter = "Monitor profiles (*.json)|*.json|All files (*.*)|*.*",
            Title = "Save Profile",
        };
        if (dlg.ShowDialog(this) != true) return;

        var name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        try
        {
            ProfileService.Save(_monitors, dlg.FileName, name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save profile:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Monitor profiles (*.json)|*.json|All files (*.*)|*.*",
            Title = "Load Profile",
        };
        if (dlg.ShowDialog(this) != true) return;

        List<ProfileService.ProfileEntry> entries;
        string name;
        try
        {
            (entries, name) = ProfileService.Load(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not read profile:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var matched = ProfileService.ApplyToMonitors(entries, _monitors);
        if (matched.Count == 0)
        {
            MessageBox.Show(this, "No monitors in this profile matched the current display setup.",
                "No match", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PushUndo();
        foreach (var (monitor, x, y) in matched)
        {
            monitor.X = x;
            monitor.Y = y;
        }
        foreach (var r in _rows)
            r.NotifyMovedFromCanvas();
        Canvas.Refresh();
        if (!string.IsNullOrEmpty(name))
            Title = $"Monitor Arrangement — {name}";
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Canvas.ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Canvas.ZoomOut();
    private void Fit_Click(object sender, RoutedEventArgs e) => Canvas.FitView();

    // ------------------------------------------------------- snap/grid + settings

    private void SnapEdges_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        Canvas.SnapEdges = SnapEdgesCheck.IsChecked == true;
        StoreSetting("snapEdges", JsonSerializer.SerializeToElement(Canvas.SnapEdges));
    }

    private void SnapGrid_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        Canvas.SnapGrid = SnapGridCheck.IsChecked == true;
        StoreSetting("snapGrid", JsonSerializer.SerializeToElement(Canvas.SnapGrid));
        Canvas.Redraw();
    }

    private void GridSize_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_loaded) return;
        if (int.TryParse(GridSizeBox.Text.Trim(), out var gs) && gs > 0)
        {
            Canvas.GridSize = gs;
            StoreSetting("gridSize", JsonSerializer.SerializeToElement(gs));
            if (Canvas.SnapGrid) Canvas.Redraw();
        }
    }

    /// <summary>Apply settings pushed from the host over IPC (no re-save; host owns the file).</summary>
    public void ApplySettings(Dictionary<string, JsonElement> values)
    {
        if (values.TryGetValue("snapEdges", out var se) && se.ValueKind is JsonValueKind.True or JsonValueKind.False)
            SnapEdgesCheck.IsChecked = se.GetBoolean();
        if (values.TryGetValue("snapGrid", out var sg) && sg.ValueKind is JsonValueKind.True or JsonValueKind.False)
            SnapGridCheck.IsChecked = sg.GetBoolean();
        if (values.TryGetValue("gridSize", out var gz) && gz.TryGetInt32(out var n) && n > 0)
            GridSizeBox.Text = n.ToString();
        Canvas.Redraw();
    }

    public void SurfaceWindow()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ApplyStoredSettingsToUi()
    {
        var state = _settings.GetModule(ModuleId);

        bool snapGrid = GetBool(state, "snapGrid", true);
        bool snapEdges = GetBool(state, "snapEdges", true);
        int gridSize = GetInt(state, "gridSize", AppConstants.DefaultGrid);

        Canvas.SnapGrid = snapGrid;
        Canvas.SnapEdges = snapEdges;
        Canvas.GridSize = gridSize;

        SnapGridCheck.IsChecked = snapGrid;
        SnapEdgesCheck.IsChecked = snapEdges;
        GridSizeBox.Text = gridSize.ToString();
        Canvas.Redraw();
    }

    private void StoreSetting(string key, JsonElement value)
    {
        _settings.GetModule(ModuleId).Settings[key] = value;
        _settings.Save();
    }

    private static bool GetBool(ModuleState state, string key, bool fallback) =>
        state.Settings.TryGetValue(key, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : fallback;

    private static int GetInt(ModuleState state, string key, int fallback) =>
        state.Settings.TryGetValue(key, out var el) && el.TryGetInt32(out var n) ? n : fallback;
}
