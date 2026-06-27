using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Toolkit.Common.Hosting;
using Toolkit.Common.Manifest;
using Toolkit.Common.Settings;

namespace Toolkit.Runner.Ui;

/// <summary>
/// Renders a module's <see cref="SettingSchema"/> list into editable fields and writes
/// the values back into the shared settings store. This is the generic, no-code path
/// every module gets for free; richer config can use the module's own window instead.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ModuleManifest _manifest;
    private readonly ModuleSupervisor _supervisor;
    private readonly ModuleState _state;
    private readonly List<Func<(string key, JsonElement value)>> _readers = new();

    private static readonly SolidColorBrush LabelFg = new(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static readonly SolidColorBrush FieldFg = new(Colors.White);
    private static readonly SolidColorBrush FieldBg = new(Color.FromRgb(0x1A, 0x3A, 0x6E));

    public SettingsWindow(ModuleManifest manifest, ModuleSupervisor supervisor)
    {
        InitializeComponent();
        _manifest = manifest;
        _supervisor = supervisor;
        _state = supervisor.Settings.GetModule(manifest.Id);

        TitleText.Text = manifest.Name;
        SubtitleText.Text = manifest.Description;

        BuildFields();
    }

    private void BuildFields()
    {
        if (_manifest.Settings.Count == 0)
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = "This module exposes no settings.",
                Foreground = LabelFg,
            });
            return;
        }

        foreach (var schema in _manifest.Settings)
            FieldsPanel.Children.Add(BuildField(schema));
    }

    private UIElement BuildField(SettingSchema schema)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        container.Children.Add(new TextBlock
        {
            Text = schema.Label,
            Foreground = LabelFg,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
        });

        switch (schema.Type)
        {
            case "bool":
            {
                var box = new CheckBox { IsChecked = GetBool(schema), Foreground = FieldFg };
                _readers.Add(() => (schema.Key, JsonSerializer.SerializeToElement(box.IsChecked == true)));
                container.Children.Add(box);
                break;
            }
            case "int":
            {
                var box = MakeTextBox(GetString(schema));
                _readers.Add(() =>
                {
                    var raw = box.Text.Trim();
                    var value = int.TryParse(raw, out var n) ? n : 0;
                    if (schema.Min is { } min) value = Math.Max(min, value);
                    if (schema.Max is { } max) value = Math.Min(max, value);
                    return (schema.Key, JsonSerializer.SerializeToElement(value));
                });
                container.Children.Add(box);
                break;
            }
            case "enum":
            {
                var combo = new ComboBox { Foreground = Brushes.Black };
                foreach (var opt in schema.Options ?? new List<string>())
                    combo.Items.Add(opt);
                combo.SelectedItem = GetString(schema);
                _readers.Add(() => (schema.Key, JsonSerializer.SerializeToElement(combo.SelectedItem as string ?? "")));
                container.Children.Add(combo);
                break;
            }
            case "path":
            {
                var row = new DockPanel();
                var box = MakeTextBox(GetString(schema));
                var browse = new Button { Content = "…", Width = 32, Margin = new Thickness(6, 0, 0, 0) };
                browse.Click += (_, _) =>
                {
                    var dlg = new OpenFileDialog();
                    if (dlg.ShowDialog() == true)
                        box.Text = dlg.FileName;
                };
                DockPanel.SetDock(browse, Dock.Right);
                row.Children.Add(browse);
                row.Children.Add(box);
                _readers.Add(() => (schema.Key, JsonSerializer.SerializeToElement(box.Text)));
                container.Children.Add(row);
                break;
            }
            default: // "string"
            {
                var box = MakeTextBox(GetString(schema));
                _readers.Add(() => (schema.Key, JsonSerializer.SerializeToElement(box.Text)));
                container.Children.Add(box);
                break;
            }
        }

        return container;
    }

    private TextBox MakeTextBox(string text) => new()
    {
        Text = text,
        Foreground = FieldFg,
        Background = FieldBg,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(6, 4, 6, 4),
        CaretBrush = FieldFg,
    };

    private bool GetBool(SettingSchema schema)
    {
        if (_state.Settings.TryGetValue(schema.Key, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return el.GetBoolean();
        return schema.Default?.ValueKind == JsonValueKind.True;
    }

    private string GetString(SettingSchema schema)
    {
        if (_state.Settings.TryGetValue(schema.Key, out var el))
            return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString();
        if (schema.Default is { } d)
            return d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.ToString();
        return "";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        foreach (var reader in _readers)
        {
            var (key, value) = reader();
            _state.Settings[key] = value;
        }

        await _supervisor.ApplySettingsAsync(_manifest);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
