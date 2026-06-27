using System.Windows.Media;

namespace MonitorArrangement;

/// <summary>Numeric tunables and the per-monitor color palette (ported from constants.py).</summary>
public static class AppConstants
{
    public const double CanvasPadding = 30;
    public const double SnapThresholdPx = 14;
    public const int UndoMax = 30;
    public const int DefaultGrid = 120;

    public static readonly Color[] MonitorColors =
    {
        Color.FromRgb(0x4E, 0x9A, 0xF1),
        Color.FromRgb(0xF1, 0x71, 0x4E),
        Color.FromRgb(0x4E, 0xF1, 0x9A),
        Color.FromRgb(0xF1, 0xC9, 0x4E),
        Color.FromRgb(0xA0, 0x4E, 0xF1),
        Color.FromRgb(0xF1, 0x4E, 0xA0),
        Color.FromRgb(0x4E, 0xF1, 0xF1),
        Color.FromRgb(0xF1, 0xA0, 0x4E),
    };

    public static Color MonitorColor(int index) => MonitorColors[index % MonitorColors.Length];

    public static Brush MonitorBrush(int index)
    {
        var brush = new SolidColorBrush(MonitorColor(index));
        brush.Freeze();
        return brush;
    }
}
