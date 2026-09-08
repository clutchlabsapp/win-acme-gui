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
    private readonly Label _wacsStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly TextBox _email = Ui.Text();
    private readonly CheckBox _acceptTos = new() { Text = "I accept the ACME terms of service", AutoSize = true };
    private readonly CheckBox _testServer = new() { Text = "Use the Let's Encrypt staging server (test certificates)", AutoSize = true };

    private readonly TextBox _friendlyName = Ui.Text();
    private readonly TextBox _commonName = Ui.Text();
    private readonly TextBox _hostNames = Ui.Text(multiline: true, height: 90);

    private readonly ComboBox _challengeMode = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _validationPlugin = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
    };

    /// <summary>The plugins currently listed, so the dropdown index means something.</summary>
    private readonly List<ValidationPlugin> _shownPlugins = [];

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
    private Button _installButton = null!;
    private TabControl _tabs = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private Button _saveCredentials = null!;
    private Button _reloadCredentials = null!;
    private FlowLayoutPanel _credentialActions = null!;

    /// <summary>What wacs.exe reports about itself, or null when it is not installed.</summary>
    private WacsInstallation? _installation;

    private CancellationTokenSource? _running;
    private bool _loading = true;

    /// <summary>Set while the code updates a control itself, to stop the change handlers recursing.</summary>
    private bool _updatingUi;

    public MainForm()
    {
        Text = "win-acme GUI";
        // Sized for the tallest tab — Azure DNS has seven fields, After renewal six —
        // so no page needs its scrollbar at a normal font scale.
        MinimumSize = new Size(720, 560);
        Size = new Size(880, 700);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        ApplySettings();

        _loading = false;
        RefreshPreview();
    }

    /// <summary>
    /// Five tabs following the order of the job: set win-acme up, choose how ownership
    /// is proved, say what the certificate covers, say what happens afterwards, then
    /// verify and run.
    /// </summary>
    private void BuildUi()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill };

        _tabs.TabPages.AddRange(
        [
            Ui.Page("win-acme", BuildWinAcmeGroup(), BuildAccountGroup()),
            Ui.Page("Validation", BuildValidationGroup()),
            Ui.Page("Certificate", BuildCertificateGroup(), BuildStoreGroup()),
            Ui.Page("After renewal", BuildInstallationGroup()),
            Ui.FillPage("Verify and run", BuildActionArea()),
        ]);

        // Problems are raised on any tab but only listed on the last one, so a
        // one-line summary stays visible from everywhere.
        _statusStrip = new StatusStrip { SizingGrip = false };
        _statusLabel = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(_tabs);
        Controls.Add(_statusStrip);
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

        _installButton = Ui.Button("Download and install win-acme...", OnInstallWinAcme);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
        };

        actions.Controls.Add(_installButton);

        Ui.AddFullWidth(grid, _wacsStatus);
        Ui.AddFullWidth(grid, actions);

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

        _challengeMode.Items.AddRange(["DNS (dns-01)", "HTTP (http-01)"]);
        _challengeMode.SelectedIndexChanged += OnChallengeModeChanged;
        _validationPlugin.SelectedIndexChanged += OnValidationPluginChanged;
        _pluginFields.ValuesChanged += OnInputChanged;

        Ui.AddRow(grid, "Challenge", _challengeMode,
            "DNS works for wildcards and needs no inbound HTTP. HTTP needs the ACME server to reach "
            + "this machine on port 80, and cannot issue wildcards.");
        Ui.AddRow(grid, "Provider", _validationPlugin);
        Ui.AddFullWidth(grid, _pluginFields);

        _saveCredentials = Ui.Button("Save credentials to appsettings.json", OnSaveNamecheapCredentials);
        _reloadCredentials = Ui.Button("Reload", OnReloadNamecheapCredentials);

        _credentialActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Visible = false,
        };

        _credentialActions.Controls.AddRange([_saveCredentials, _reloadCredentials]);
        Ui.AddFullWidth(grid, _credentialActions);

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

    private ValidationPlugin? SelectedPlugin =>
        _validationPlugin.SelectedIndex >= 0 && _validationPlugin.SelectedIndex < _shownPlugins.Count
            ? _shownPlugins[_validationPlugin.SelectedIndex]
            : null;

    private string SelectedChallenge =>
        _challengeMode.SelectedIndex == 1 ? PluginCatalog.HttpChallenge : PluginCatalog.DnsChallenge;

    /// <summary>
    /// Rebuilds the provider list for the chosen challenge, keeping the current
    /// provider selected when it offers that challenge too.
    /// </summary>
    private void ShowProvidersFor(string challenge, string? preferredId)
    {
        _shownPlugins.Clear();
        _shownPlugins.AddRange(PluginCatalog.ForChallenge(challenge));

        var wasLoading = _loading;
        _loading = true;

        try
        {
            _validationPlugin.Items.Clear();
            _validationPlugin.Items.AddRange([.. _shownPlugins.Select(p => (object)p.DisplayName)]);

            var index = _shownPlugins.FindIndex(
                p => string.Equals(p.Id, preferredId, StringComparison.OrdinalIgnoreCase));

            _validationPlugin.SelectedIndex = _shownPlugins.Count == 0 ? -1 : Math.Max(index, 0);
        }
        finally
        {
            _loading = wasLoading;
        }

        OnValidationPluginChanged(this, EventArgs.Empty);
    }

    private void OnChallengeModeChanged(object? sender, EventArgs e) =>
        ShowProvidersFor(SelectedChallenge, SelectedPlugin?.Id);

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

        var remembered = PluginCatalog.Find(_settings.ValidationPluginId);
        var challenge = remembered?.ValidationMode ?? PluginCatalog.DnsChallenge;

        _challengeMode.SelectedIndex =
            string.Equals(challenge, PluginCatalog.HttpChallenge, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        ShowProvidersFor(challenge, remembered?.Id);
        UpdateCredentialActions();

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

        UpdateCredentialActions();
        UpdateInstallationStatus();
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
        problems.AddRange(CheckScriptHelper());

        _problems.Text = problems.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, problems.Select(p => "• " + p));

        ShowProblemSummary(problems);

        _preview.Text = WacsArgumentBuilder
            .BuildCreateRenewal(wacsPath.Length == 0 ? "wacs.exe" : wacsPath, definition)
            .ToDisplayString();

        var idle = _running is null;
        _createButton.Enabled = idle && problems.Count == 0;
        _installButton.Enabled = idle;
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
            _ = RefreshInstallationAsync();
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
                .RunAsync(_wacsPath.Text.Trim(), SelectedPlugin, token)
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

    /// <summary>
    /// Keeps the bottom strip in step with the problem list, so an error raised on one
    /// tab is still visible while looking at another.
    /// </summary>
    private void ShowProblemSummary(IReadOnlyList<string> problems)
    {
        if (problems.Count == 0)
        {
            _statusLabel.Text = "Ready.";
            _statusLabel.ForeColor = SystemColors.ControlText;
            return;
        }

        _statusLabel.Text = problems.Count == 1
            ? problems[0]
            : $"{problems[0]}  (+{problems.Count - 1} more on Verify and run)";

        _statusLabel.ForeColor = Color.Firebrick;
    }

    // ------------------------------------------------------ namecheap helper

    /// <summary>
    /// Shows the credential buttons only for a provider whose credentials live in an
    /// external file, and loads whatever is already in that file.
    /// </summary>
    private void UpdateCredentialActions()
    {
        var wiring = SelectedPlugin?.ScriptWiring;
        _credentialActions.Visible = wiring is not null;

        if (wiring is null)
        {
            return;
        }

        LoadNamecheapCredentials();
    }

    private void LoadNamecheapCredentials()
    {
        var wiring = SelectedPlugin?.ScriptWiring;
        if (wiring is null)
        {
            return;
        }

        var executable = _pluginFields.ValueOf(wiring.ExecutableFieldName);
        var credentials = NamecheapSettingsFile.Read(executable);

        if (credentials == NamecheapCredentials.Empty)
        {
            return;
        }

        _updatingUi = true;
        try
        {
            _pluginFields.SetValue("apikey", credentials.ApiKey);
            _pluginFields.SetValue("username", credentials.UserName);
            _pluginFields.SetValue("apiusername", credentials.ApiUserName);
            _pluginFields.SetValue("clientip", credentials.ClientIp);
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void OnReloadNamecheapCredentials(object? sender, EventArgs e)
    {
        LoadNamecheapCredentials();
        RefreshPreview();
        AppendLog("Reloaded credentials from appsettings.json.");
    }

    /// <summary>
    /// Writes the credentials, but only when explicitly asked and only after showing
    /// exactly which file is about to be written. This is the one place the tool puts
    /// a secret on disk, and it does so because the helper has no other way to read it.
    /// </summary>
    private void OnSaveNamecheapCredentials(object? sender, EventArgs e)
    {
        var wiring = SelectedPlugin?.ScriptWiring;
        if (wiring is null)
        {
            return;
        }

        var executable = _pluginFields.ValueOf(wiring.ExecutableFieldName);
        if (executable.Length == 0)
        {
            MessageBox.Show(
                this,
                $"Choose {wiring.ExpectedFileName} first — the settings file is written next to it.",
                "Save credentials",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var path = NamecheapSettingsFile.ExpectedPath(executable);

        var confirmed = MessageBox.Show(
            this,
            string.Join(Environment.NewLine,
            [
                "Write your Namecheap API credentials to:",
                string.Empty,
                path,
                string.Empty,
                "The API key is stored there in plain text. That is how the helper reads it,",
                "so protect the folder with file permissions.",
            ]),
            "Save credentials",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirmed != DialogResult.OK)
        {
            return;
        }

        try
        {
            NamecheapSettingsFile.Write(executable, new NamecheapCredentials(
                _pluginFields.ValueOf("apikey"),
                _pluginFields.ValueOf("username"),
                _pluginFields.ValueOf("apiusername"),
                _pluginFields.ValueOf("clientip")));

            AppendLog($"Wrote credentials to {path}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"Could not write {path}:{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Save credentials",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        RefreshPreview();
    }

    /// <summary>
    /// Checks the things about the helper that Core cannot: that it exists, that its
    /// settings file is filled in, and that it sits where win-acme can actually run it.
    /// </summary>
    private IEnumerable<string> CheckScriptHelper()
    {
        var wiring = SelectedPlugin?.ScriptWiring;
        if (wiring is null)
        {
            return Array.Empty<string>();
        }

        var executable = _pluginFields.ValueOf(wiring.ExecutableFieldName);
        if (executable.Length == 0)
        {
            return Array.Empty<string>();
        }

        var problems = new List<string>();

        if (!File.Exists(executable))
        {
            problems.Add($"{wiring.ExpectedFileName} not found at {executable}.");
            return problems;
        }

        // win-acme's ScriptClient never sets a working directory, and the helper loads
        // appsettings.json relative to the current directory rather than from beside
        // itself. If it is not in the win-acme folder it throws before reaching the API.
        var wacsFolder = Path.GetDirectoryName(_wacsPath.Text.Trim());
        var helperFolder = Path.GetDirectoryName(executable);

        if (!string.IsNullOrEmpty(wacsFolder)
            && !string.IsNullOrEmpty(helperFolder)
            && !string.Equals(
                Path.GetFullPath(wacsFolder).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(helperFolder).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"Move {wiring.ExpectedFileName} and its {NamecheapSettingsFile.FileName} into the "
                + "win-acme folder. It reads its settings from the working directory, and win-acme "
                + "runs scripts from its own folder.");
        }

        var settingsPath = NamecheapSettingsFile.ExpectedPath(executable);
        if (!File.Exists(settingsPath))
        {
            problems.Add($"{settingsPath} does not exist. Fill in the credentials and press Save.");
            return problems;
        }

        var missing = NamecheapSettingsFile.Read(executable).MissingValues;
        if (missing.Count > 0)
        {
            problems.Add($"{NamecheapSettingsFile.FileName} is missing: {string.Join(", ", missing)}.");
        }

        return problems;
    }

    // ------------------------------------------------------- win-acme install

    /// <summary>
    /// Asks the installed wacs.exe what it is. Runs on show and after anything that
    /// could change the answer, rather than on every keystroke.
    /// </summary>
    private async Task RefreshInstallationAsync()
    {
        var path = _wacsPath.Text.Trim();

        if (path.Length == 0)
        {
            _installation = null;
            UpdateInstallationStatus();
            return;
        }

        _wacsStatus.Text = "Checking win-acme...";
        _wacsStatus.ForeColor = SystemColors.GrayText;

        try
        {
            _installation = await new WacsInspector(_runner).InspectAsync(path).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _installation = null;
            AppendLog($"Could not inspect win-acme: {exception.Message}");
        }

        UpdateInstallationStatus();
    }

    private void UpdateInstallationStatus()
    {
        var plugin = SelectedPlugin;

        if (_installation is null)
        {
            SetStatus("win-acme was not found. Browse to it, or download it below.", problem: true);
            _installButton.Text = "Download and install win-acme...";
            return;
        }

        _installButton.Text = "Download or repair win-acme...";

        if (!_installation.SupportsPlugins)
        {
            // Every DNS provider offered here is an external plugin, and the trimmed
            // build cannot load any of them.
            SetStatus(
                $"{_installation.Description} — this build cannot load DNS plugins. Install the pluggable build.",
                problem: true);
            return;
        }

        if (plugin is { RequiresSeparateDownload: true } && !_installation.HasPlugin(plugin.Id))
        {
            SetStatus(
                $"{_installation.Description} — the {plugin.DisplayName} plugin is not installed.",
                problem: true);
            return;
        }

        SetStatus(
            plugin is null
                ? _installation.Description
                : $"{_installation.Description} — {plugin.DisplayName} plugin ready.",
            problem: false);
    }

    private void SetStatus(string text, bool problem)
    {
        _wacsStatus.Text = text;
        _wacsStatus.ForeColor = problem ? Color.Firebrick : SystemColors.GrayText;
    }

    private async void OnInstallWinAcme(object? sender, EventArgs e)
    {
        var plugin = SelectedPlugin;

        await WithBusyAsync("Installing win-acme", async token =>
        {
            var installer = new WinAcmeInstaller();

            AppendLog("Looking up the latest win-acme release on GitHub...");
            var release = await installer.FetchLatestReleaseAsync(token).ConfigureAwait(true);

            var main = release.MainPackage(WinAcmeInstaller.CurrentArchitecture);
            if (main is null)
            {
                AppendLog($"Release {release.TagName} has no {WinAcmeInstaller.CurrentArchitecture} build.");
                return;
            }

            var pluginAsset = plugin is { RequiresSeparateDownload: true }
                ? release.DnsPlugin(plugin.Id)
                : null;
            var folder = ChooseInstallFolder();

            if (folder is null)
            {
                AppendLog("Cancelled.");
                return;
            }

            if (!ConfirmInstall(release, main, pluginAsset, folder))
            {
                AppendLog("Cancelled.");
                return;
            }

            var pluginIds = plugin is not null && pluginAsset is not null
                ? new[] { plugin.Id }
                : Array.Empty<string>();

            var installed = await installer
                .InstallAsync(release, folder, pluginIds, new Progress<string>(AppendLog), token)
                .ConfigureAwait(true);

            AppendLog($"Installed win-acme {release.Version} to {folder}.");

            _wacsPath.Text = installed;
            await RefreshInstallationAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private string? ChooseInstallFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Where should win-acme be installed? It must be a permanent folder, "
                          + "because the scheduled task runs it from here.",
            UseDescriptionForTitle = true,
            SelectedPath = _installation?.Path is { Length: > 0 } existing
                ? Path.GetDirectoryName(existing) ?? WinAcmeInstaller.DefaultInstallFolder
                : WinAcmeInstaller.DefaultInstallFolder,
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private bool ConfirmInstall(
        WinAcmeRelease release,
        WinAcmeAsset main,
        WinAcmeAsset? pluginAsset,
        string folder)
    {
        // Built as lines rather than with embedded escapes, so the message stays
        // readable and there is nothing to get wrong about line endings.
        var lines = new List<string>
        {
            $"Download win-acme {release.Version} from github.com/win-acme/win-acme",
            "and unpack it to:",
            string.Empty,
            folder,
            string.Empty,
            "Files:",
            $"    {main.Name}  ({main.SizeDescription})",
        };

        if (pluginAsset is not null)
        {
            lines.Add($"    {pluginAsset.Name}  ({pluginAsset.SizeDescription})");
        }

        lines.Add(string.Empty);
        lines.Add("The pluggable build is used because the DNS validation plugins do not");
        lines.Add("work on the smaller trimmed build. Existing files will be overwritten.");

        if (SelectedPlugin is { RequiresSeparateDownload: true } && pluginAsset is null)
        {
            lines.Add(string.Empty);
            lines.Add($"Note: this release has no separate download for {SelectedPlugin.DisplayName}.");
        }

        return MessageBox.Show(
                   this,
                   string.Join(Environment.NewLine, lines),
                   "Install win-acme",
                   MessageBoxButtons.OKCancel,
                   MessageBoxIcon.Question)
               == DialogResult.OK;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RefreshInstallationAsync().ConfigureAwait(true);
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
