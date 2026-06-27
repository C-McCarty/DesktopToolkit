using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Toolkit.Common.Services;

namespace MonitorArrangement.Models;

/// <summary>
/// Binds one <see cref="MonitorInfo"/> to a sidebar row. X/Y are two-way editable:
/// editing here raises <see cref="PositionEdited"/> so the canvas redraws; the canvas
/// updates the underlying model directly and calls <see cref="NotifyMovedFromCanvas"/>
/// so the fields refresh without re-triggering a redraw loop.
/// </summary>
public sealed class MonitorRow : INotifyPropertyChanged
{
    private readonly int _ordinal;

    public MonitorRow(MonitorInfo info, int ordinal)
    {
        Info = info;
        _ordinal = ordinal;
    }

    public MonitorInfo Info { get; }

    public int Ordinal => _ordinal;
    public int Index => Info.Index;
    public string FriendlyName => Info.FriendlyName;
    public string Title => $"{Info.Index}   {Info.FriendlyName}";

    public string ResolutionLine =>
        $"{Info.Width}×{Info.Height} @ {Info.RefreshRate} Hz" + (Info.IsPrimary ? "   ★ Primary" : "");

    public Brush ColorBrush => AppConstants.MonitorBrush(_ordinal);

    public int X
    {
        get => Info.X;
        set
        {
            if (Info.X == value) return;
            Info.X = value;
            OnPropertyChanged();
            PositionEdited?.Invoke();
        }
    }

    public int Y
    {
        get => Info.Y;
        set
        {
            if (Info.Y == value) return;
            Info.Y = value;
            OnPropertyChanged();
            PositionEdited?.Invoke();
        }
    }

    /// <summary>Raised when X/Y are changed from the sidebar (not from a canvas drag).</summary>
    public event Action? PositionEdited;

    /// <summary>Refresh the bound fields after the canvas moved the model.</summary>
    public void NotifyMovedFromCanvas()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
