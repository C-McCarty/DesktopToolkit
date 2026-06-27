using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Toolkit.Common.Services;

namespace MonitorArrangement.Interop;

/// <summary>
/// Briefly flashes a big number on each physical monitor so the user can match the
/// "Display N" rows to real screens (ported from the Python _identify()). Windows are
/// positioned in PHYSICAL pixels via MoveWindow because the process is per-monitor DPI
/// aware (WPF Left/Top are DIPs and would mis-place on scaled displays).
/// </summary>
public static class IdentifyOverlay
{
    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

    public static void Show(IReadOnlyList<MonitorInfo> monitors, int milliseconds = 3000)
    {
        var windows = new List<Window>();

        foreach (var m in monitors)
        {
            double fontSize = Math.Max(40, Math.Min(200, m.Height / 5.0));
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                AllowsTransparency = true,
                Opacity = 0.75,
                Background = Brushes.Black,
                Content = new TextBlock
                {
                    Text = m.Index.ToString(),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontWeight = FontWeights.Bold,
                    FontSize = fontSize,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var monitor = m; // capture
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                MoveWindow(hwnd, monitor.X, monitor.Y, monitor.Width, monitor.Height, true);
            };

            window.Show();
            windows.Add(window);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var w in windows)
            {
                try { w.Close(); } catch { /* ignore */ }
            }
        };
        timer.Start();
    }
}
