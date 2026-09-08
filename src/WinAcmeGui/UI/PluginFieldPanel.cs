using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;

namespace WinAcmeGui.UI;

/// <summary>
/// Renders the parameters of whichever validation plugin is selected. Everything it
/// draws comes from <see cref="PluginCatalog"/>, so supporting another plugin means
/// adding a catalog entry and nothing here.
/// </summary>
internal sealed class PluginFieldPanel : Panel
{
    private readonly Dictionary<string, Control> _inputs = new(StringComparer.OrdinalIgnoreCase);

    private ValidationPlugin? _plugin;

    public PluginFieldPanel()
    {
        Dock = DockStyle.Fill;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    /// <summary>Raised whenever the user edits any field, so the preview can refresh.</summary>
    public event EventHandler? ValuesChanged;

    public void ShowPlugin(ValidationPlugin plugin, IReadOnlyDictionary<string, string>? initialValues = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        _plugin = plugin;
        _inputs.Clear();

        SuspendLayout();

        // Snapshot first: disposing a control removes it from Controls, which would
        // otherwise mutate the collection being enumerated.
        foreach (var existing in Controls.Cast<Control>().ToList())
        {
            existing.Dispose();
        }

        Controls.Clear();

        var grid = Ui.Grid();

        if (!string.IsNullOrEmpty(plugin.Description))
        {
            Ui.AddHint(grid, plugin.Description);
        }

        foreach (var field in plugin.Fields)
        {
            var initial = initialValues is not null && initialValues.TryGetValue(field.Name, out var value)
                ? value
                : string.Empty;

            AddField(grid, field, initial);
        }

        Controls.Add(grid);
        ResumeLayout(performLayout: true);
    }

    /// <summary>Copies what the user typed into the settings object the builder reads.</summary>
    public void ApplyTo(ValidationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.PluginId = _plugin?.Id ?? string.Empty;
        settings.Values.Clear();

        foreach (var (name, control) in _inputs)
        {
            settings.Values[name] = control switch
            {
                CheckBox check => check.Checked ? "true" : "false",
                TextBox text => text.Text.Trim(),
                _ => string.Empty,
            };
        }
    }

    /// <summary>The control holding a field's value, so the form can read or set one.</summary>
    public Control? ControlFor(string fieldName) =>
        _inputs.TryGetValue(fieldName, out var control) ? control : null;

    public string ValueOf(string fieldName) => ControlFor(fieldName) switch
    {
        CheckBox check => check.Checked ? "true" : "false",
        TextBox text => text.Text.Trim(),
        _ => string.Empty,
    };

    public void SetValue(string fieldName, string value)
    {
        switch (ControlFor(fieldName))
        {
            case CheckBox check:
                check.Checked = bool.TryParse(value, out var enabled) && enabled;
                break;
            case TextBox text:
                text.Text = value;
                break;
        }
    }

    private void AddField(TableLayoutPanel grid, PluginField field, string initialValue)
    {
        if (field.Kind is PluginFieldKind.FilePath or PluginFieldKind.FolderPath)
        {
            AddPathField(grid, field, initialValue);
            return;
        }

        if (field.Kind == PluginFieldKind.Boolean)
        {
            var check = new CheckBox
            {
                Text = field.Label,
                AutoSize = true,
                Checked = bool.TryParse(initialValue, out var enabled) && enabled,
            };

            check.CheckedChanged += RaiseValuesChanged;
            _inputs[field.Name] = check;
            Ui.AddFullWidth(grid, check, field.Help);
            return;
        }

        var input = new TextBox
        {
            Text = initialValue,
            UseSystemPasswordChar = field.IsSecret,
        };

        input.TextChanged += RaiseValuesChanged;
        _inputs[field.Name] = input;

        var label = field.Required ? field.Label + " *" : field.Label;
        Ui.AddRow(grid, label, input, field.Help);
    }

    private void AddPathField(TableLayoutPanel grid, PluginField field, string initialValue)
    {
        var input = new TextBox { Text = initialValue, Dock = DockStyle.Fill };
        input.TextChanged += RaiseValuesChanged;
        _inputs[field.Name] = input;

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };

        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(input, 0, 0);
        row.Controls.Add(Ui.Button("Browse...", (_, _) => Browse(field, input)), 1, 0);

        Ui.AddRow(grid, field.Required ? field.Label + " *" : field.Label, row, field.Help);
    }

    private void Browse(PluginField field, TextBox input)
    {
        if (field.Kind == PluginFieldKind.FolderPath)
        {
            BrowseForFolder(field, input);
            return;
        }

        var expected = _plugin?.ScriptWiring?.ExpectedFileName ?? string.Empty;

        using var dialog = new OpenFileDialog
        {
            Title = "Locate " + (expected.Length > 0 ? expected : field.Label),
            Filter = expected.Length > 0
                ? $"{expected}|{expected}|Programs (*.exe)|*.exe|All files (*.*)|*.*"
                : "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        var current = input.Text.Trim();
        var directory = current.Length > 0 ? Path.GetDirectoryName(current) : null;
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            input.Text = dialog.FileName;
        }
    }

    private void BrowseForFolder(PluginField field, TextBox input)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = field.Help.Length > 0 ? field.Help : field.Label,
            UseDescriptionForTitle = true,
        };

        var current = input.Text.Trim();
        if (current.Length > 0 && Directory.Exists(current))
        {
            dialog.SelectedPath = current;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            input.Text = dialog.SelectedPath;
        }
    }

    private void RaiseValuesChanged(object? sender, EventArgs e) =>
        ValuesChanged?.Invoke(this, EventArgs.Empty);
}
