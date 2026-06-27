using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MonitorArrangement.Models;

namespace MonitorArrangement.Controls;

/// <summary>
/// Draggable monitor layout view. A faithful WPF port of the Python <c>canvas_view.py</c>:
/// virtual→canvas transform with auto-fit, wheel-zoom about the cursor, right-drag pan,
/// edge snap and grid snap, and a grid overlay.
/// </summary>
public sealed class MonitorCanvas : FrameworkElement
{
    private IReadOnlyList<MonitorRow> _rows = Array.Empty<MonitorRow>();

    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;
    private double _fitScale = 1.0;

    private bool _userZoomed;
    private Point? _panAnchor;

    private int? _dragging;
    private Point _dragAnchorVirt;
    private Point _dragAnchorMouse;

    // --- Visual resources ---
    private static readonly Brush CanvasBg = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)));
    private static readonly Pen BoundsPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44)), 1)
    {
        DashStyle = new DashStyle(new double[] { 4, 4 }, 0),
    });
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x4A)), 1));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Typeface LabelFace = new("Segoe UI");

    public MonitorCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public bool SnapEdges { get; set; } = true;
    public bool SnapGrid { get; set; } = true;
    public int GridSize { get; set; } = AppConstants.DefaultGrid;

    /// <summary>Raised when a drag begins, so the host window can snapshot for undo.</summary>
    public event Action? DragStarted;

    public void LoadMonitors(IReadOnlyList<MonitorRow> rows)
    {
        _rows = rows;
        _userZoomed = false;
        Fit();
        InvalidateVisual();
    }

    /// <summary>Refit and redraw — used after external model changes (profile load, reset).</summary>
    public void Refresh()
    {
        _userZoomed = false;
        Fit();
        InvalidateVisual();
    }

    /// <summary>Redraw without refitting — keeps the viewport stable during edits.</summary>
    public void Redraw() => InvalidateVisual();

    public void FitView()
    {
        _userZoomed = false;
        Fit();
        InvalidateVisual();
    }

    public void ZoomIn() => ZoomAt(1.25, ActualWidth / 2, ActualHeight / 2);

    public void ZoomOut() => ZoomAt(0.8, ActualWidth / 2, ActualHeight / 2);

    // ----------------------------------------------------------------- transform

    private void Fit()
    {
        if (_rows.Count == 0) return;

        double w = ActualWidth > 0 ? ActualWidth : 800;
        double h = ActualHeight > 0 ? ActualHeight : 500;

        double minX = _rows.Min(r => r.Info.X);
        double minY = _rows.Min(r => r.Info.Y);
        double maxX = _rows.Max(r => r.Info.X + r.Info.Width);
        double maxY = _rows.Max(r => r.Info.Y + r.Info.Height);

        double virtW = (maxX - minX) == 0 ? 1 : maxX - minX;
        double virtH = (maxY - minY) == 0 ? 1 : maxY - minY;

        double availW = w - 2 * AppConstants.CanvasPadding;
        double availH = h - 2 * AppConstants.CanvasPadding;
        _scale = Math.Min(availW / virtW, availH / virtH);

        _offsetX = AppConstants.CanvasPadding + (availW - virtW * _scale) / 2 - minX * _scale;
        _offsetY = AppConstants.CanvasPadding + (availH - virtH * _scale) / 2 - minY * _scale;
        _fitScale = _scale;
    }

    private void ZoomAt(double factor, double px, double py)
    {
        if (_rows.Count == 0) return;

        double newScale = _scale * factor;
        double lo = _fitScale * 0.2, hi = _fitScale * 25;
        newScale = Math.Max(lo, Math.Min(hi, newScale));
        factor = newScale / _scale;
        if (Math.Abs(factor - 1.0) < 1e-9) return;

        _offsetX = px - (px - _offsetX) * factor;
        _offsetY = py - (py - _offsetY) * factor;
        _scale = newScale;
        _userZoomed = true;
        InvalidateVisual();
    }

    private Point Vc(double x, double y) => new(x * _scale + _offsetX, y * _scale + _offsetY);

    // ---------------------------------------------------------------------- draw

    protected override void OnRender(DrawingContext dc)
    {
        // Fill the whole surface so the entire control is hit-testable for panning.
        dc.DrawRectangle(CanvasBg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_rows.Count == 0) return;

        double minX = _rows.Min(r => r.Info.X);
        double minY = _rows.Min(r => r.Info.Y);
        double maxX = _rows.Max(r => r.Info.X + r.Info.Width);
        double maxY = _rows.Max(r => r.Info.Y + r.Info.Height);

        var topLeft = Vc(minX, minY);
        var bottomRight = Vc(maxX, maxY);
        dc.DrawRectangle(null, BoundsPen, new Rect(topLeft, bottomRight));

        if (SnapGrid && GridSize > 0)
            DrawGrid(dc, minX, minY, maxX, maxY);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int i = 0; i < _rows.Count; i++)
        {
            var m = _rows[i].Info;
            var p0 = Vc(m.X, m.Y);
            var p1 = Vc(m.X + m.Width, m.Y + m.Height);
            var rect = new Rect(p0, p1);

            double outlineW = i == _dragging ? 3 : 2;
            var fill = AppConstants.MonitorBrush(_rows[i].Ordinal);
            dc.DrawRectangle(fill, new Pen(Brushes.White, outlineW), rect);

            if (rect.Width >= 40 && rect.Height >= 24)
            {
                var lines = new List<string> { $"{m.Index}: {m.FriendlyName}", $"{m.Width}×{m.Height}" };
                if (m.IsPrimary) lines.Add("[Primary]");
                double fontSize = Math.Max(7, Math.Min(11, rect.Height / 7));
                var text = new FormattedText(
                    string.Join("\n", lines), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    LabelFace, fontSize, LabelBrush, dpi)
                {
                    TextAlignment = TextAlignment.Center,
                    MaxTextWidth = rect.Width,
                };
                dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2,
                                            rect.Y + (rect.Height - text.Height) / 2));
            }
        }
    }

    private void DrawGrid(DrawingContext dc, double minX, double minY, double maxX, double maxY)
    {
        int gs = GridSize;
        double gx = Math.Floor(minX / gs) * gs;
        while (gx <= maxX)
        {
            var top = Vc(gx, minY);
            var bottom = Vc(gx, maxY);
            dc.DrawLine(GridPen, top, bottom);
            gx += gs;
        }
        double gy = Math.Floor(minY / gs) * gs;
        while (gy <= maxY)
        {
            var left = Vc(minX, gy);
            var right = Vc(maxX, gy);
            dc.DrawLine(GridPen, left, right);
            gy += gs;
        }
    }

    // -------------------------------------------------------------------- events

    private int? MonitorAt(Point p)
    {
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            var m = _rows[i].Info;
            var p0 = Vc(m.X, m.Y);
            var p1 = Vc(m.X + m.Width, m.Y + m.Height);
            if (p.X >= p0.X && p.X <= p1.X && p.Y >= p0.Y && p.Y <= p1.Y)
                return i;
        }
        return null;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (!_userZoomed) Fit();
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        ZoomAt(e.Delta > 0 ? 1.1 : 0.9, pos.X, pos.Y);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            FitView();
            return;
        }

        var pos = e.GetPosition(this);
        var hit = MonitorAt(pos);
        if (hit is null) return;

        DragStarted?.Invoke();
        _dragging = hit;
        var m = _rows[hit.Value].Info;
        _dragAnchorVirt = new Point(m.X, m.Y);
        _dragAnchorMouse = pos;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging is null) return;

        var pos = e.GetPosition(this);
        var row = _rows[_dragging.Value];
        var m = row.Info;
        double threshold = AppConstants.SnapThresholdPx / _scale;

        double dx = (pos.X - _dragAnchorMouse.X) / _scale;
        double dy = (pos.Y - _dragAnchorMouse.Y) / _scale;

        int newX = (int)(_dragAnchorVirt.X + dx);
        int newY = (int)(_dragAnchorVirt.Y + dy);

        if (SnapGrid && GridSize > 0)
        {
            newX = (int)(Math.Round((double)newX / GridSize) * GridSize);
            newY = (int)(Math.Round((double)newY / GridSize) * GridSize);
        }

        if (SnapEdges)
            (newX, newY) = Snap(_dragging.Value, newX, newY, threshold);

        m.X = newX;
        m.Y = newY;
        row.NotifyMovedFromCanvas();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging is null) return;
        _dragging = null;
        ReleaseMouseCapture();
        if (!_userZoomed) Fit();
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        _panAnchor = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        _panAnchor = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _panAnchor = null;
    }

    // Right-drag panning is handled here because OnMouseMove fires for any button.
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_panAnchor is null || e.RightButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(this);
        _offsetX += pos.X - _panAnchor.Value.X;
        _offsetY += pos.Y - _panAnchor.Value.Y;
        _panAnchor = pos;
        _userZoomed = true;
        InvalidateVisual();
    }

    private (int x, int y) Snap(int idx, int x, int y, double threshold)
    {
        var m = _rows[idx].Info;
        int bestX = x, bestY = y;
        double minDx = threshold + 1, minDy = threshold + 1;

        for (int i = 0; i < _rows.Count; i++)
        {
            if (i == idx) continue;
            var o = _rows[i].Info;

            foreach (var sx in new[] { o.X, o.X + o.Width, o.X - m.Width, o.X + o.Width - m.Width })
            {
                if (Math.Abs(x - sx) < minDx) { bestX = sx; minDx = Math.Abs(x - sx); }
            }
            foreach (var sy in new[] { o.Y, o.Y + o.Height, o.Y - m.Height, o.Y + o.Height - m.Height })
            {
                if (Math.Abs(y - sy) < minDy) { bestY = sy; minDy = Math.Abs(y - sy); }
            }
        }

        return (bestX, bestY);
    }

    private static TBrush Freeze<TBrush>(TBrush f) where TBrush : Freezable
    {
        f.Freeze();
        return f;
    }
}
