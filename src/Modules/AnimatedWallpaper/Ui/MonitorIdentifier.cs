using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AnimatedDesktopBackground.Interop;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace AnimatedDesktopBackground.Ui;

/// <summary>
/// Briefly shows a large number on each connected display (like Windows' "Identify"), so the user
/// knows which "Display N" in the manager maps to which physical screen. These are ordinary topmost
/// WPF windows (not WorkerW children), so WPF renders into them normally.
/// </summary>
internal static class MonitorIdentifier
{
    private static readonly List<Window> Open = new();
    private static DispatcherTimer? _timer;

    public static void Show(TimeSpan? duration = null)
    {
        Close();
        foreach (var m in MonitorService.GetMonitors())
        {
            var win = BuildWindow(m);
            Open.Add(win);
            win.Show();
        }

        _timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Close();
        _timer.Start();
    }

    public static void Close()
    {
        _timer?.Stop();
        _timer = null;
        foreach (var w in Open)
        {
            try { w.Close(); } catch { /* ignore */ }
        }
        Open.Clear();
    }

    private static Window BuildWindow(MonitorInfo m)
    {
        var number = new TextBlock
        {
            Text = (m.Index + 1).ToString(),
            FontSize = 240,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = m.DisplayLabel,
            FontSize = 24,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD6)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -10, 0, 0)
        };
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(number);
        panel.Children.Add(label);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x88, 0xE5)),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(60, 30, 60, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = panel
        };

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)),
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = card
        };

        // Position to the monitor's physical bounds (PerMonitorV2 → MoveWindow uses physical pixels).
        win.SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(win).Handle;
            var b = m.Bounds;
            NativeMethods.MoveWindow(hwnd, b.Left, b.Top, b.Width, b.Height, true);
        };
        win.MouseDown += (_, _) => Close(); // click anywhere to dismiss early
        return win;
    }
}
