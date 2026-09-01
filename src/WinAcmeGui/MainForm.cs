using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;
using WinAcmeGui.UI;

namespace WinAcmeGui;

/// <summary>
/// The whole tool: fill in the fields, see the wacs.exe command that will run, run it,
/// and check that what came out will actually renew.
/// </summary>
public sealed class MainForm : Form
{
    private const int MaxLogLines = 5000;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly IWacsRunner _runner = new WacsRunner();

    private readonly TextBox _wacsPath = Ui.Text();
    private readonly TextBox _email = Ui.Text();
    private readonly CheckBox _acceptTos = new() { Text = "I accept the ACME terms of service", AutoSize = true };
    private readonly CheckBox _testServer = new() { Text = "Use the Let's Encrypt staging server (test certificates)", AutoSize = true };

    private readonly TextBox _friendlyName = Ui.Text();
    private readonly TextBox _commonName = Ui.Text();
    private readonly TextBox _hostNames = Ui.Text(multiline: true, height: 90);

    private readonly ComboBox _validationPlugin = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
    };

    private readonly PluginFieldPanel _pluginFields = new();

    private readonly ComboBox _storeName = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly CheckBox _keepExisting = new() { Text = "Keep the previous certificate when renewing", AutoSize = true };

    private readonly CheckBox _updateIis = new() { Text = "Rebind IIS https bindings to the new certificate", AutoSize = true };
    private readonly TextBox _iisSiteId = Ui.Text();
    private readonly TextBox _sslPort = Ui.Text();
    private readonly TextBox _sslIpAddress = Ui.Text();

    private readonly ComboBox _scriptPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _scriptPath = Ui.Text();
    private readonly TextBox _scriptParameters = Ui.Text();

    private readonly TextBox _preview = Ui.ReadOnlyPane(70);
    private readonly TextBox _log = Ui.ReadOnlyPane(180);
    private readonly Label _problems = new()
    {
        AutoSize = true,
        ForeColor = Color.Firebrick,
        Margin = new Padding(0, 0, 0, Ui.Gap),
    };

    private Button _createButton = null!;
    private Button _checkButton = null!;
    private Button _renewButton = null!;
    private Button _cancelButton = null!;

    private Button _browseScript = null!;

    private CancellationTokenSource? _running;
    private bool _loading = true;

    /// <summary>Set while the code updates a control itself, to stop the change handlers recursing.</summary>
    private bool _updatingUi;

    public MainForm()
    {
        Text = "win-acme GUI";
        MinimumSize = new Size(780, 620);
        Size = new Size(940, 800);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        ApplySettings();

        _loading = false;
        RefreshPreview();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(Ui.Gap),
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));

        root.Controls.Add(BuildSettingsArea(), 0, 0);
        root.Controls.Add(BuildActionArea(), 0, 1);

        Controls.Add(root);
    }

    private Control BuildSettingsArea()
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        foreach (var group in new[]
        {
            BuildWinAcmeGroup(),
            BuildAccountGroup(),
            BuildCertificateGroup(),
            BuildValidationGroup(),
            BuildStoreGroup(),
            BuildInstallationGroup(),
            BuildPreviewGroup(),
        })
        {
            stack.Controls.Add(group, 0, stack.RowCount);
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowCount++;
        }

        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroller.Controls.Add(stack);
        return scroller;
    }

    private GroupBox BuildWinAcmeGroup()
    {
        var grid = Ui.Grid();

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };

        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _wacsPath.Dock = DockStyle.Fill;
        pathRow.Controls.Add(_wacsPath, 0, 0);
        pathRow.Controls.Add(Ui.Button("Browse...", OnBrowse), 1, 0);

        _wacsPath.TextChanged += OnInputChanged;

        Ui.AddRow(grid, "wacs.exe", pathRow, "The win-acme executable. Found automatically in the usual locations.");

        return Ui.Group("win-acme", grid);
    }

    private GroupBox BuildAccountGroup()
    {
        var grid = Ui.Grid();

        _email.TextChanged += OnInputChanged;
        _acceptTos.CheckedChanged += OnInputChanged;
        _testServer.CheckedChanged += OnInputChanged;

        Ui.AddRow(grid, "Email address *", _email, "Let's Encrypt sends expiry warnings here.");
        Ui.AddFullWidth(grid, _acceptTos);
        Ui.AddFullWidth(
            grid,
            _testServer,
            "Staging certificates are not trusted by browsers, but staging is not rate limited. "
            + "Prove the setup here first.");

        return Ui.Group("ACME account", grid);
    }

    private GroupBox BuildCertificateGroup()
    {
        var grid = Ui.Grid();

        _friendlyName.TextChanged += OnInputChanged;
        _commonName.TextChanged += OnInputChanged;
        _hostNames.TextChanged += OnInputChanged;

        Ui.AddRow(grid, "Host names *", _hostNames,
            "One per line, or comma separated. Wildcards such as *.example.com are allowed.");
        Ui.AddRow(grid, "Common name", _commonName,
            "Optional. Must be one of the host names above; win-acme defaults to the first one.");
        Ui.AddRow(grid, "Friendly name", _friendlyName,
            "How this renewal is labelled in win-acme and in the certificate store.");

        return Ui.Group("Certificate", grid);
    }

    private GroupBox BuildValidationGroup()
    {
        var grid = Ui.Grid();

        _validationPlugin.Items.AddRange([.. PluginCatalog.ValidationPlugins.Select(p => (object)p.DisplayName)]);
        _validationPlugin.SelectedIndexChanged += OnValidationPluginChanged;
        _pluginFields.ValuesChanged += OnInputChanged;

        Ui.AddRow(grid, "DNS provider", _validationPlugin,
            "DNS validation is used throughout: it works for wildcards and needs no inbound HTTP.");
        Ui.AddFullWidth(grid, _pluginFields);

        return Ui.Group("Validation", grid);
    }

    private GroupBox BuildStoreGroup()
    {
        var grid = Ui.Grid();

        _storeName.Items.AddRange(["", "WebHosting", "My"]);
        _storeName.TextChanged += OnInputChanged;
        _keepExisting.CheckedChanged += OnInputChanged;

        Ui.AddRow(grid, "Certificate store", _storeName,
            "Blank uses win-acme's default (WebHosting). Choose My for the RDP import scripts, "
            + "which only look in LocalMachine\\My.");
        Ui.AddFullWidth(grid, _keepExisting);

        return Ui.Group("Certificate store", grid);
    }

    private GroupBox BuildInstallationGroup()
    {
        var grid = Ui.Grid();

        _updateIis.CheckedChanged += OnInputChanged;
        _iisSiteId.TextChanged += OnInputChanged;
        _sslPort.TextChanged += OnInputChanged;
        _sslIpAddress.TextChanged += OnInputChanged;
        _scriptPath.TextChanged += OnInputChanged;
        _scriptParameters.TextChanged += OnInputChanged;

        _scriptPreset.Items.AddRange([.. InstallationPresets.Scripts.Select(p => (object)p.DisplayName)]);
        _scriptPreset.SelectedIndexChanged += OnScriptPresetChanged;

        Ui.AddFullWidth(grid, _updateIis);
        Ui.AddRow(grid, "IIS site ID", _iisSiteId, "Optional. Blank installs to the site the binding belongs to.");
        Ui.AddRow(grid, "HTTPS port", _sslPort, "Optional. Blank means 443.");
        Ui.AddRow(grid, "HTTPS IP address", _sslIpAddress, "Optional. Blank means all addresses.");

        Ui.AddRow(grid, "Run after renewal", _scriptPreset,
            "The Remote Desktop and Exchange scripts ship inside win-acme's own Scripts folder.");

        var scriptRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };

        scriptRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        scriptRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _scriptPath.Dock = DockStyle.Fill;
        _browseScript = Ui.Button("Browse...", OnBrowseScript);

        scriptRow.Controls.Add(_scriptPath, 0, 0);
        scriptRow.Controls.Add(_browseScript, 1, 0);

        Ui.AddRow(grid, "Script", scriptRow);
        Ui.AddRow(grid, "Parameters", _scriptParameters,
            "Tokens: " + string.Join("  ", InstallationPresets.ParameterTokens));

        return Ui.Group("After renewal", grid);
    }

    private GroupBox BuildPreviewGroup()
    {
        var grid = Ui.Grid();

        Ui.AddFullWidth(grid, _preview);
        Ui.AddHint(grid, "Credentials are hidden here. Use Copy command to put the real command line on the clipboard.");

        return Ui.Group("Command that will run", grid);
    }

    private Control BuildActionArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };

        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _createButton = Ui.Button("Create renewal", OnCreateRenewal, isDefault: true);
        _checkButton = Ui.Button("Check setup", OnCheckSetup);
        _renewButton = Ui.Button("Renew now", OnRenewNow);
        _cancelButton = Ui.Button("Stop", OnStop);
        _cancelButton.Enabled = false;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, Ui.Gap),
        };

        buttons.Controls.AddRange(
        [
            _createButton,
            _checkButton,
            _renewButton,
            _cancelButton,
            Ui.Button("Copy command", OnCopyCommand),
            Ui.Button("Clear log", (_, _) => _log.Clear()),
        ]);

        area.Controls.Add(_problems, 0, 0);
        area.Controls.Add(buttons, 0, 1);
        area.Controls.Add(Ui.GroupFill("Output", _log), 0, 2);

        return area;
    }

    // ---------------------------------------------------------------- state

    private static ValidationPlugin? PluginAt(int index) =>
        index >= 0 && index < PluginCatalog.ValidationPlugins.Count
            ? PluginCatalog.ValidationPlugins[index]
            : null;

    private ValidationPlugin? SelectedPlugin => PluginAt(_validationPlugin.SelectedIndex);

    private InstallationScriptPreset SelectedScriptPreset =>
        _scriptPreset.SelectedIndex >= 0 && _scriptPreset.SelectedIndex < InstallationPresets.Scripts.Count
            ? InstallationPresets.Scripts[_scriptPreset.SelectedIndex]
            : InstallationPresets.None;

    private void ApplySettings()
    {
        _wacsPath.Text = WacsLocator.Locate(_settings.WacsPath) ?? _settings.WacsPath;
        _email.Text = _settings.EmailAddress;
        _acceptTos.Checked = _settings.AcceptTermsOfService;
        _testServer.Checked = _settings.UseTestServer;
        _friendlyName.Text = _settings.FriendlyName;
        _commonName.Text = _settings.CommonName;
        _hostNames.Text = string.Join(Environment.NewLine, _settings.HostNames);
        _storeName.Text = _settings.CertificateStoreName;
        _keepExisting.Checked = _settings.KeepExisting;

        _updateIis.Checked = _settings.Installation.UpdateIisBindings;
        _iisSiteId.Text = _settings.Installation.IisSiteId;
        _sslPort.Text = _settings.Installation.SslPort;
        _sslIpAddress.Text = _settings.Installation.SslIpAddress;

        var presetIndex = InstallationPresets.Scripts
            .ToList()
            .FindIndex(p => string.Equals(p.Id, _settings.Installation.ScriptPresetId, StringComparison.OrdinalIgnoreCase));

        // Selecting a preset resets the parameters to that preset's defaults, so the
        // remembered values have to go in afterwards, not before.
        _scriptPreset.SelectedIndex = presetIndex >= 0 ? presetIndex : 0;

        if (_settings.Installation.ScriptParameters.Length > 0)
        {
            _scriptParameters.Text = _settings.Installation.ScriptParameters;
        }

        if (_settings.Installation.ScriptPath.Length > 0)
        {
            _scriptPath.Text = _settings.Installation.ScriptPath;
        }

        UpdateScriptControls();

        var index = PluginCatalog.ValidationPlugins
            .ToList()
            .FindIndex(p => string.Equals(p.Id, _settings.ValidationPluginId, StringComparison.OrdinalIgnoreCase));

        _validationPlugin.SelectedIndex = index >= 0 ? index : 0;

        // Selecting the plugin above fires the changed handler, which draws the fields;
        // draw them again with the remembered values now that the handler has run.
        if (SelectedPlugin is { } plugin)
        {
            _pluginFields.ShowPlugin(plugin, _settings.ValidationValues);
        }
    }

    private void SaveSettings()
    {
        var definition = CurrentDefinition();

        _settings.WacsPath = _wacsPath.Text.Trim();
        _settings.EmailAddress = definition.Account.EmailAddress;
        _settings.AcceptTermsOfService = definition.Account.AcceptTermsOfService;
        _settings.UseTestServer = definition.Account.UseTestServer;
        _settings.FriendlyName = definition.Certificate.FriendlyName;
        _settings.CommonName = definition.Certificate.CommonName;
        _settings.HostNames = definition.Certificate.HostNames;
        _settings.CertificateStoreName = definition.Store.StoreName;
        _settings.KeepExisting = definition.Store.KeepExisting;
        _settings.Installation = definition.Installation;
        _settings.RememberValidationValues(definition.Validation);

        _settings.Save();
    }

    private RenewalDefinition CurrentDefinition()
    {
        var definition = new RenewalDefinition
        {
            Account = new AcmeAccount
            {
                EmailAddress = _email.Text.Trim(),
                AcceptTermsOfService = _acceptTos.Checked,
                UseTestServer = _testServer.Checked,
            },
            Certificate = new CertificateRequest
            {
                FriendlyName = _friendlyName.Text.Trim(),
                CommonName = _commonName.Text.Trim(),
                HostNames = HostNameParser.Parse(_hostNames.Text),
            },
            Store = new StoreSettings
            {
                StoreName = _storeName.Text.Trim(),
                KeepExisting = _keepExisting.Checked,
            },
            Installation = new InstallationSettings
            {
                UpdateIisBindings = _updateIis.Checked,
                IisSiteId = _iisSiteId.Text.Trim(),
                SslPort = _sslPort.Text.Trim(),
                SslIpAddress = _sslIpAddress.Text.Trim(),
                ScriptPresetId = SelectedScriptPreset.Id,
                ScriptPath = _scriptPath.Text.Trim(),
                ScriptParameters = _scriptParameters.Text.Trim(),
            },
        };

        _pluginFields.ApplyTo(definition.Validation);
        return definition;
    }

    private void OnInputChanged(object? sender, EventArgs e) => RefreshPreview();

    private void OnValidationPluginChanged(object? sender, EventArgs e)
    {
        if (SelectedPlugin is { } plugin)
        {
            var remembered = string.Equals(plugin.Id, _settings.ValidationPluginId, StringComparison.OrdinalIgnoreCase)
                ? _settings.ValidationValues
                : null;

            _pluginFields.ShowPlugin(plugin, remembered);
        }

        RefreshPreview();
    }

    private void OnScriptPresetChanged(object? sender, EventArgs e)
    {
        var preset = SelectedScriptPreset;

        _updatingUi = true;
        try
        {
            _scriptParameters.Text = preset.DefaultParameters;

            if (!preset.IsCustom)
            {
                _scriptPath.Text = string.Empty;
            }

            // These scripts only search LocalMachine\My, so a blank store — which means
            // WebHosting — would leave them finding nothing. Filling in a blank field is
            // a help; overwriting a deliberate choice is not, so only do the former.
            if (preset.RequiredStoreName.Length > 0 && _storeName.Text.Trim().Length == 0)
            {
                _storeName.Text = preset.RequiredStoreName;
            }
        }
        finally
        {
            _updatingUi = false;
        }

        UpdateScriptControls();
        RefreshPreview();
    }

    private void UpdateScriptControls()
    {
        var preset = SelectedScriptPreset;

        _scriptPath.Enabled = !preset.IsNone;
        _scriptPath.ReadOnly = !preset.IsCustom;
        _browseScript.Enabled = preset.IsCustom;
        _scriptParameters.Enabled = !preset.IsNone;
    }

    private void OnBrowseScript(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the script to run after renewal",
            Filter = "Scripts and programs (*.ps1;*.bat;*.cmd;*.exe)|*.ps1;*.bat;*.cmd;*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _scriptPath.Text = dialog.FileName;
        }
    }

    private void RefreshPreview()
    {
        if (_loading || _updatingUi)
        {
            return;
        }

        var definition = CurrentDefinition();
        var problems = RenewalValidator.Validate(definition).ToList();

        var wacsPath = _wacsPath.Text.Trim();
        if (wacsPath.Length == 0)
        {
            problems.Insert(0, "Point the tool at wacs.exe.");
        }

        problems.AddRange(ShowResolvedScriptPath(definition));

        _problems.Text = problems.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, problems.Select(p => "• " + p));

        _preview.Text = WacsArgumentBuilder
            .BuildCreateRenewal(wacsPath.Length == 0 ? "wacs.exe" : wacsPath, definition)
            .ToDisplayString();

        var idle = _running is null;
        _createButton.Enabled = idle && problems.Count == 0;
        _checkButton.Enabled = idle;
        _renewButton.Enabled = idle && wacsPath.Length > 0;
    }

    /// <summary>
    /// Bundled presets live next to wacs.exe, so their path is derived rather than
    /// typed. Shows it in the read-only box and reports it if the file is not there —
    /// a missing script only fails at the very end of a renewal otherwise.
    /// </summary>
    private IEnumerable<string> ShowResolvedScriptPath(RenewalDefinition definition)
    {
        var preset = SelectedScriptPreset;
        if (preset.IsNone)
        {
            return Array.Empty<string>();
        }

        var resolved = WacsArgumentBuilder.ResolveScriptPath(WacsPathOrDefault(), preset, definition.Installation);

        if (!preset.IsCustom && _scriptPath.Text != resolved)
        {
            _updatingUi = true;
            _scriptPath.Text = resolved;
            _updatingUi = false;
        }

        return resolved.Length > 0 && !File.Exists(resolved)
            ? new[] { $"Script not found: {resolved}" }
            : Array.Empty<string>();
    }

    // ---------------------------------------------------------------- actions

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Locate wacs.exe",
            Filter = "win-acme (wacs.exe)|wacs.exe|Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };

        var current = _wacsPath.Text.Trim();
        var directory = current.Length > 0 ? Path.GetDirectoryName(current) : null;
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _wacsPath.Text = dialog.FileName;
        }
    }

    private void OnCopyCommand(object? sender, EventArgs e)
    {
        var command = WacsArgumentBuilder.BuildCreateRenewal(WacsPathOrDefault(), CurrentDefinition());

        if (command.ContainsSecrets)
        {
            var answer = MessageBox.Show(
                this,
                "This command contains your API credentials in plain text.\r\n\r\n"
                + "Copy it with the credentials included?",
                "Copy command",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (answer == DialogResult.Cancel)
            {
                return;
            }

            Clipboard.SetText(command.ToDisplayString(redactSecrets: answer == DialogResult.No));
        }
        else
        {
            Clipboard.SetText(command.ToDisplayString(redactSecrets: false));
        }

        AppendLog("Command copied to the clipboard.");
    }

    private void OnStop(object? sender, EventArgs e)
    {
        _running?.Cancel();
        AppendLog("Stopping...");
    }

    private async void OnCreateRenewal(object? sender, EventArgs e)
    {
        var definition = CurrentDefinition();
        var command = WacsArgumentBuilder.BuildCreateRenewal(WacsPathOrDefault(), definition);

        var target = string.Join(", ", definition.Certificate.HostNames);
        var server = definition.Account.UseTestServer ? "the Let's Encrypt STAGING server" : "Let's Encrypt";

        var confirmed = MessageBox.Show(
            this,
            $"Request a certificate for:\r\n\r\n{target}\r\n\r\nfrom {server}, and save the renewal?",
            "Create renewal",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (confirmed != DialogResult.OK)
        {
            return;
        }

        SaveSettings();
        await WithBusyAsync("Creating renewal", token => RunAndLogAsync(command, token));
    }

    private async void OnRenewNow(object? sender, EventArgs e)
    {
        var friendlyName = _friendlyName.Text.Trim();

        var scope = friendlyName.Length > 0
            ? $"the renewal named '{friendlyName}'"
            : "every renewal that is due";

        var confirmed = MessageBox.Show(
            this,
            $"Renew {scope} now?\r\n\r\n"
            + "This forces a renewal even if the certificate is not due yet. Let's Encrypt "
            + "rate limits apply, so avoid repeating it against the live server.",
            "Renew now",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirmed != DialogResult.OK)
        {
            return;
        }

        var command = WacsArgumentBuilder.BuildRenewNow(
            WacsPathOrDefault(),
            friendlyName: friendlyName.Length > 0 ? friendlyName : null,
            force: true,
            useTestServer: _testServer.Checked);

        await WithBusyAsync("Renewing", token => RunAndLogAsync(command, token));
    }

    private async void OnCheckSetup(object? sender, EventArgs e)
    {
        await WithBusyAsync("Checking setup", async token =>
        {
            var report = await new SetupChecker(_runner)
                .RunAsync(_wacsPath.Text.Trim(), token)
                .ConfigureAwait(true);

            foreach (var check in report.Checks)
            {
                AppendLog($"[{check.Status.ToString().ToUpperInvariant(),-4}] {check.Name}: {check.Detail}");
            }

            if (report.Renewals.Count > 0)
            {
                AppendLog(string.Empty);
                AppendLog("Configured renewals:");

                foreach (var renewal in report.Renewals)
                {
                    var due = renewal.IsDue ? "due now" : $"due {renewal.DueDescription}";
                    var errors = renewal.HasErrors ? $", {renewal.Errors} error(s)" : string.Empty;
                    AppendLog($"  {renewal.FriendlyName} — {due}{errors}");
                }
            }

            AppendLog(string.Empty);
            AppendLog(report.AllPassed
                ? "Everything checks out: these certificates will renew automatically."
                : "Some checks did not pass. Fix the items marked FAIL above.");
        });
    }

    // ---------------------------------------------------------------- running

    private string WacsPathOrDefault()
    {
        var path = _wacsPath.Text.Trim();
        return path.Length > 0 ? path : "wacs.exe";
    }

    private async Task WithBusyAsync(string title, Func<CancellationToken, Task> work)
    {
        if (_running is not null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _running = cancellation;
        SetBusy(busy: true);
        LogHeader(title);

        try
        {
            await work(cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Stopped.");
        }
        catch (Exception exception)
        {
            AppendLog($"Error: {exception.Message}");
        }
        finally
        {
            _running = null;
            SetBusy(busy: false);
        }
    }

    private async Task RunAndLogAsync(WacsCommand command, CancellationToken cancellationToken)
    {
        AppendLog(command.ToDisplayString());
        AppendLog(string.Empty);

        var result = await _runner
            .RunAsync(command, AppendFromBackground, cancellationToken)
            .ConfigureAwait(true);

        AppendLog(string.Empty);
        AppendLog(result.Succeeded
            ? "wacs.exe finished successfully."
            : $"wacs.exe exited with code {result.ExitCode}.");
    }

    private void SetBusy(bool busy)
    {
        _cancelButton.Enabled = busy;
        UseWaitCursor = busy;
        RefreshPreview();
    }

    // ---------------------------------------------------------------- logging

    private void LogHeader(string title)
    {
        AppendLog(string.Empty);
        AppendLog($"=== {title} — {DateTime.Now:HH:mm:ss} ===");
    }

    /// <summary>Called from the process reader threads, so it hops back to the UI thread.</summary>
    private void AppendFromBackground(string line)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => AppendLog(line));
        }
        catch (ObjectDisposedException)
        {
            // The window closed while wacs.exe was still writing. Nothing to show it on.
        }
        catch (InvalidOperationException)
        {
            // Same race, seen when the handle goes away between the check and the call.
        }
    }

    private void AppendLog(string line)
    {
        if (_log.Lines.Length > MaxLogLines)
        {
            _log.Lines = [.. _log.Lines.Skip(_log.Lines.Length - (MaxLogLines / 2))];
        }

        _log.AppendText(line + Environment.NewLine);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _running?.Cancel();
        SaveSettings();
        base.OnFormClosing(e);
    }
}
