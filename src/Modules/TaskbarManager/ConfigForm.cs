namespace TaskbarManager;

/// <summary>
/// Per-monitor show/hide window (the managed-mode replacement for the old tray menu).
/// A checked row means that monitor's taskbar is visible; unchecking hides it and
/// reclaims the space. Changes apply immediately and persist to the shared store.
/// </summary>
public sealed class ConfigForm : Form
{
    private readonly ModuleSettings _settings;
    private readonly Action _onChanged;
    private readonly FlowLayoutPanel _list;
    private bool _building;

    private static readonly Color Bg = Color.FromArgb(0x16, 0x21, 0x3E);
    private static readonly Color Fg = Color.White;
    private static readonly Color Muted = Color.FromArgb(0xAA, 0xAA, 0xAA);

    public ConfigForm(ModuleSettings settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;

        Text = "Taskbar Manager";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(420, 360);
        MinimumSize = new Size(360, 240);
        BackColor = Color.FromArgb(0x0D, 0x0D, 0x1A);
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = true;

        var header = new Label
        {
            Text = "Show or hide the taskbar on each monitor",
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 12, 12, 4),
            ForeColor = Muted,
        };

        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12, 4, 12, 8),
            BackColor = Color.FromArgb(0x0D, 0x0D, 0x1A),
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Bg };
        var showAll = new Button
        {
            Text = "Show all taskbars",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Fg,
            BackColor = Color.FromArgb(0x1E, 0x2A, 0x4A),
            Location = new Point(12, 8),
        };
        showAll.FlatAppearance.BorderSize = 0;
        showAll.Click += (_, _) =>
        {
            foreach (var device in _settings.HiddenMonitors.ToList())
                _settings.Toggle(device);
            _onChanged();
            BuildList();
        };
        footer.Controls.Add(showAll);

        Controls.Add(_list);
        Controls.Add(header);
        Controls.Add(footer);

        BuildList();
    }

    private void BuildList()
    {
        _building = true;
        _list.Controls.Clear();

        var barsByDevice = TaskbarController.Enumerate()
            .Where(b => !string.IsNullOrEmpty(b.DeviceName))
            .Select(b => b.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int index = 0;
        foreach (Screen screen in Screen.AllScreens)
        {
            index++;
            bool hasBar = barsByDevice.Contains(screen.DeviceName);
            bool hidden = _settings.IsHidden(screen.DeviceName);
            string device = screen.DeviceName;

            var check = new CheckBox
            {
                Text = $"Display {index} — {screen.Bounds.Width}×{screen.Bounds.Height}"
                       + (screen.Primary ? "  (Primary)" : string.Empty),
                Checked = hasBar && !hidden,
                Enabled = hasBar,
                ForeColor = hasBar ? Fg : Muted,
                AutoSize = true,
                Margin = new Padding(2, 4, 2, 4),
            };

            if (!hasBar)
                check.Text += "   (no taskbar on this display)";

            check.CheckedChanged += (_, _) =>
            {
                if (_building) return;
                _settings.Toggle(device); // checked->visible, unchecked->hidden
                _onChanged();
            };

            _list.Controls.Add(check);
        }

        _building = false;
    }
}
