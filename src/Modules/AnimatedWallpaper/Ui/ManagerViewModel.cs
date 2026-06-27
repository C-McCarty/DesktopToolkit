using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using AnimatedDesktopBackground.Models;

namespace AnimatedDesktopBackground.Ui;

internal abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>One monitor checkbox within an assignment row.</summary>
internal sealed class MonitorToggleVm : ObservableObject
{
    private bool _isChecked;

    public required string DeviceName { get; init; }
    public required string Label { get; init; }

    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value)) Toggled?.Invoke(this); }
    }

    /// <summary>Change the state without raising <see cref="Toggled"/> (avoids coordinator recursion).</summary>
    public void SetCheckedSilently(bool value)
    {
        if (_isChecked == value) return;
        _isChecked = value;
        Raise(nameof(IsChecked));
    }

    public event Action<MonitorToggleVm>? Toggled;
}

/// <summary>An editable media assignment spanning one or more monitors.</summary>
internal sealed class AssignmentRowVm : ObservableObject
{
    private string? _mediaPath;
    private bool _muted = true;
    private FillMode _fillMode = FillMode.Cover;

    public ObservableCollection<MonitorToggleVm> Monitors { get; } = new();

    public string? MediaPath
    {
        get => _mediaPath;
        set { if (Set(ref _mediaPath, value)) Raise(nameof(MediaDisplay)); }
    }

    public string MediaDisplay =>
        string.IsNullOrEmpty(MediaPath) ? "(no media — click Browse…)" : Path.GetFileName(MediaPath);

    public bool Muted { get => _muted; set => Set(ref _muted, value); }
    public FillMode FillMode { get => _fillMode; set => Set(ref _fillMode, value); }
    public Array FillModes => Enum.GetValues(typeof(FillMode));
}

/// <summary>Backing model for the manager window.</summary>
internal sealed class ManagerViewModel : ObservableObject
{
    private bool _pauseOnFullscreen;
    private bool _startMinimized;
    private bool _playOnStartup;
    private MediaEngineKind _engine;

    public ObservableCollection<AssignmentRowVm> Assignments { get; } = new();

    public bool PauseOnFullscreen { get => _pauseOnFullscreen; set => Set(ref _pauseOnFullscreen, value); }
    public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }
    public bool PlayOnStartup { get => _playOnStartup; set => Set(ref _playOnStartup, value); }

    public Array Engines => Enum.GetValues(typeof(MediaEngineKind));
    public MediaEngineKind Engine { get => _engine; set => Set(ref _engine, value); }
}
