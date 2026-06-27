using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Toolkit.Common.Hosting;
using Toolkit.Runner.Ui;

namespace Toolkit.Runner.Tray;

/// <summary>Owns the single suite tray icon and its menu, and the dashboard lifetime.</summary>
public sealed class TrayController : IDisposable
{
    private const uint VK_D = 0x44;

    private readonly ModuleSupervisor _supervisor;
    private TaskbarIcon? _tray;
    private GlobalHotkey? _hotkey;
    private DashboardWindow? _dashboard;

    public TrayController(ModuleSupervisor supervisor) => _supervisor = supervisor;

    public void Show()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "Desktop Toolkit  (Ctrl+Alt+D)",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/tray.ico")),
            ContextMenu = BuildMenu(),
        };
        // Left-click (and double-click) opens the dashboard; right-click shows the menu.
        _tray.TrayLeftMouseUp += (_, _) => OpenDashboard();
        _tray.TrayMouseDoubleClick += (_, _) => OpenDashboard();

        // Reaches the dashboard even if the tray icon itself is hidden (e.g. the primary
        // taskbar was hidden by Taskbar Manager).
        _hotkey = new GlobalHotkey(VK_D, OpenDashboard);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open Dashboard", InputGestureText = "Ctrl+Alt+D" };
        open.Click += (_, _) => OpenDashboard();
        menu.Items.Add(open);

        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exit);

        return menu;
    }

    public void OpenDashboard()
    {
        Diagnostics.Logger.Info("OpenDashboard requested.");
        if (_dashboard is null)
        {
            _dashboard = new DashboardWindow(_supervisor);
            Application.Current.MainWindow = _dashboard;
            _dashboard.Closed += (_, _) => _dashboard = null;
            _dashboard.Show();
        }
        else
        {
            _dashboard.Activate();
        }
    }

    public void Dispose()
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
    }
}
