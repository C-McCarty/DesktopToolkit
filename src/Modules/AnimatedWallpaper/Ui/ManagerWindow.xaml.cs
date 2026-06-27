using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AnimatedDesktopBackground.Models;
using AnimatedDesktopBackground.Wallpaper;

namespace AnimatedDesktopBackground.Ui;

/// <summary>Management GUI: assign media to one or more displays and configure global options.</summary>
public partial class ManagerWindow : Window
{
    private readonly SettingsService _settings;
    private readonly WallpaperManager _wallpaper;
    private readonly ManagerViewModel _vm = new();

    internal ManagerWindow(SettingsService settings, WallpaperManager wallpaper)
    {
        InitializeComponent();
        _settings = settings;
        _wallpaper = wallpaper;
        LoadFromSettings();
        DataContext = _vm;
    }

    private void LoadFromSettings()
    {
        var s = _settings.Settings;
        _vm.PauseOnFullscreen = s.PauseOnFullscreen;
        _vm.StartMinimized = s.StartMinimized;
        _vm.PlayOnStartup = s.PlayOnStartup;
        _vm.Engine = s.MediaEngine;
        BuildAssignments();
    }

    private void BuildAssignments()
    {
        _vm.Assignments.Clear();
        var monitors = MonitorService.GetMonitors();
        foreach (var a in _settings.Settings.Assignments)
            _vm.Assignments.Add(CreateRow(monitors, a));
    }

    private AssignmentRowVm CreateRow(List<MonitorInfo> monitors, WallpaperAssignment? a)
    {
        var row = new AssignmentRowVm
        {
            MediaPath = a?.MediaPath,
            Muted = a?.Muted ?? true,
            FillMode = a?.FillMode ?? FillMode.Cover,
        };
        foreach (var m in monitors)
        {
            var toggle = new MonitorToggleVm { DeviceName = m.DeviceName, Label = m.DisplayLabel };
            toggle.SetCheckedSilently(a?.MonitorIds.Contains(m.DeviceName) == true);
            toggle.Toggled += OnMonitorToggled;
            row.Monitors.Add(toggle);
        }
        return row;
    }

    /// <summary>Enforce that a monitor belongs to only one assignment.</summary>
    private void OnMonitorToggled(MonitorToggleVm changed)
    {
        if (!changed.IsChecked) return;
        foreach (var row in _vm.Assignments)
            foreach (var t in row.Monitors)
                if (!ReferenceEquals(t, changed) && t.DeviceName == changed.DeviceName)
                    t.SetCheckedSilently(false);
    }

    /// <summary>Called by the app when displays change while the window is open.</summary>
    internal void RefreshMonitors() => BuildAssignments();

    private void OnAddAssignment(object sender, RoutedEventArgs e) =>
        _vm.Assignments.Add(CreateRow(MonitorService.GetMonitors(), null));

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AssignmentRowVm row)
            _vm.Assignments.Remove(row);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AssignmentRowVm row) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a video, GIF, or interactive web wallpaper (.html)",
            Filter = "Wallpaper (*.mp4;*.mkv;*.webm;*.avi;*.mov;*.gif;*.html;*.htm)"
                   + "|*.mp4;*.mkv;*.webm;*.avi;*.mov;*.gif;*.html;*.htm|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) == true)
            row.MediaPath = dlg.FileName;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is AssignmentRowVm row)
            row.MediaPath = null;
    }

    private void OnIdentify(object sender, RoutedEventArgs e) => MonitorIdentifier.Show();

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _wallpaper.Stop();
        ((App)System.Windows.Application.Current).UpdateTrayState();
    }

    private void OnApplyPlay(object sender, RoutedEventArgs e)
    {
        Commit();
        if (_wallpaper.IsRunning) _wallpaper.ApplySettings();
        else _wallpaper.Start();
        ((App)System.Windows.Application.Current).UpdateTrayState();
    }

    private void OnSaveClose(object sender, RoutedEventArgs e)
    {
        Commit();
        if (_wallpaper.IsRunning) _wallpaper.ApplySettings();
        ((App)System.Windows.Application.Current).UpdateTrayState();
        Close();
    }

    /// <summary>Writes the view model back into settings and persists.</summary>
    private void Commit()
    {
        var s = _settings.Settings;
        s.PauseOnFullscreen = _vm.PauseOnFullscreen;
        s.StartMinimized = _vm.StartMinimized;
        s.PlayOnStartup = _vm.PlayOnStartup;
        s.MediaEngine = _vm.Engine;

        // Keep only assignments that have media on at least one monitor.
        s.Assignments = _vm.Assignments
            .Select(row => new WallpaperAssignment
            {
                MediaPath = row.MediaPath,
                Muted = row.Muted,
                FillMode = row.FillMode,
                MonitorIds = row.Monitors.Where(t => t.IsChecked).Select(t => t.DeviceName).ToList(),
            })
            .Where(a => !string.IsNullOrEmpty(a.MediaPath) && a.MonitorIds.Count > 0)
            .ToList();

        _settings.Save();
    }
}
