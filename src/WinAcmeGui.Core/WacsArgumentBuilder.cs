using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;

namespace WinAcmeGui.Core;

/// <summary>
/// Turns a <see cref="RenewalDefinition"/> into a wacs.exe command line. Every flag
/// emitted here is documented at https://www.win-acme.com/reference/cli.
/// </summary>
public static class WacsArgumentBuilder
{
    /// <summary>
    /// Creates (or updates) a renewal unattended. Passing <c>--source</c> is what
    /// switches win-acme out of its interactive menu.
    /// </summary>
    public static WacsCommand BuildCreateRenewal(string wacsPath, RenewalDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var args = new ArgumentList();

        // Source: which names go on the certificate.
        args.Add("--source", "manual");
        args.AddIfPresent("--host", string.Join(",", definition.Certificate.HostNames));
        args.AddIfPresent("--commonname", definition.Certificate.CommonName);
        args.AddIfPresent("--friendlyname", definition.Certificate.FriendlyName);

        // Account.
        if (definition.Account.AcceptTermsOfService)
        {
            args.Add("--accepttos");
        }

        args.AddIfPresent("--emailaddress", definition.Account.EmailAddress);

        // Validation.
        AddValidation(args, definition.Validation);

        // Store.
        args.AddIfPresent("--store", definition.Store.PluginId);
        args.AddIfPresent("--certificatestore", definition.Store.StoreName);
        if (definition.Store.KeepExisting)
        {
            args.Add("--keepexisting");
        }

        // Always say something about installation, otherwise win-acme may fall back
        // to its interactive prompt and the run hangs with nobody to answer it.
        AddInstallation(args, wacsPath, definition.Installation);

        if (definition.Account.UseTestServer)
        {
            // --closeonfinish is only meaningful together with --test, where win-acme
            // otherwise keeps the window open waiting for a keypress.
            args.Add("--test");
            args.Add("--closeonfinish");
        }

        args.Add("--verbose");

        return new WacsCommand(wacsPath, args.ToArray());
    }

    /// <summary>Suffix marking a renewal as belonging to a dry run rather than a real one.</summary>
    public const string StagingNameSuffix = "[staging test]";

    /// <summary>
    /// A dry run of the current configuration against the Let's Encrypt staging
    /// endpoint. It proves the parts that actually go wrong — the account, the
    /// validation plugin, the credentials and the challenge — without spending a live
    /// rate limit.
    /// </summary>
    /// <remarks>
    /// The definition is deliberately isolated from the real one in three ways:
    /// a distinct friendly name so it cannot overwrite the real renewal; no
    /// installation steps, because binding a staging certificate to IIS or importing
    /// it into the RDP listener would be actively harmful; and a .pfx file store, so
    /// an untrusted certificate never lands in the machine certificate store. The
    /// store and installation are already covered by the setup checks, so replacing
    /// them here costs no signal.
    /// <para>
    /// This does create and delete real DNS records — that is the point — so pair it
    /// with <see cref="BuildCancelRenewal"/> to remove the renewal afterwards.
    /// </para>
    /// </remarks>
    public static WacsCommand BuildStagingTest(string wacsPath, RenewalDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var dryRun = new RenewalDefinition
        {
            Account = new AcmeAccount
            {
                EmailAddress = definition.Account.EmailAddress,
                AcceptTermsOfService = definition.Account.AcceptTermsOfService,
                UseTestServer = true,
            },
            Certificate = new CertificateRequest
            {
                FriendlyName = StagingFriendlyName(definition),
                CommonName = definition.Certificate.CommonName,
                HostNames = [.. definition.Certificate.HostNames],
            },
            Validation = definition.Validation,
            Store = new StoreSettings
            {
                PluginId = "pfxfile",
                StoreName = string.Empty,
            },
            Installation = new InstallationSettings(),
        };

        var args = BuildCreateRenewal(wacsPath, dryRun).Arguments.ToList();

        // --pfxfilepath belongs to the pfxfile store and has no field on the model,
        // so it is spliced in after the store rather than threaded through
        // StoreSettings for the sake of one dry-run-only option.
        var storeFlag = args.FindIndex(a => a.Value == "--store");
        var insertAt = storeFlag < 0 ? args.Count : storeFlag + 2;

        args.InsertRange(insertAt,
        [
            new WacsArgument("--pfxfilepath"),
            new WacsArgument(StagingCertificateFolder),
        ]);

        return new WacsCommand(wacsPath, args);
    }

    /// <summary>Where a dry run's certificate is written; nothing reads it afterwards.</summary>
    public static string StagingCertificateFolder =>
        Path.Combine(Path.GetTempPath(), "win-acme-gui-staging");

    /// <summary>
    /// The name a dry run's renewal is stored under. Deterministic, so the cleanup
    /// afterwards can always find it again.
    /// </summary>
    public static string StagingFriendlyName(RenewalDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var basis = definition.Certificate.FriendlyName.Trim();

        if (basis.Length == 0)
        {
            basis = definition.Certificate.HostNames.FirstOrDefault() ?? "certificate";
        }

        return $"{basis} {StagingNameSuffix}";
    }

    /// <summary>
    /// Removes a renewal by name. Used to clear up after a dry run, which would
    /// otherwise stay in win-acme's store and be renewed by the scheduled task forever.
    /// </summary>
    public static WacsCommand BuildCancelRenewal(string wacsPath, string friendlyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(friendlyName);

        return new WacsCommand(
            wacsPath,
            [
                new WacsArgument("--cancel"),
                new WacsArgument("--friendlyname"),
                new WacsArgument(friendlyName.Trim()),
            ]);
    }

    /// <summary>Lists the configured renewals so the GUI can show what is set up.</summary>
    public static WacsCommand BuildList(string wacsPath) =>
        new(wacsPath, [new WacsArgument("--list")]);

    /// <summary>Prints the win-acme version, used to confirm wacs.exe actually runs.</summary>
    public static WacsCommand BuildVersion(string wacsPath) =>
        new(wacsPath, [new WacsArgument("--version")]);

    /// <summary>
    /// Lists every available argument. Loaded plugins contribute their own, so this
    /// doubles as a way to see which validation plugins are actually installed.
    /// </summary>
    public static WacsCommand BuildHelp(string wacsPath) =>
        new(wacsPath, [new WacsArgument("--help")]);

    /// <summary>
    /// Runs a renewal now. Without <paramref name="force"/> win-acme skips
    /// certificates that are not due yet, which is what the scheduled task does.
    /// </summary>
    /// <param name="renewalId">Targets one renewal by id.</param>
    /// <param name="friendlyName">
    /// Targets one renewal by name, used when the id is unknown — <c>--list</c> prints
    /// friendly names but not ids. Ignored when <paramref name="renewalId"/> is given.
    /// </param>
    public static WacsCommand BuildRenewNow(
        string wacsPath,
        string? renewalId = null,
        string? friendlyName = null,
        bool force = false,
        bool useTestServer = false)
    {
        var args = new ArgumentList();
        args.Add("--renew");

        if (!string.IsNullOrWhiteSpace(renewalId))
        {
            args.Add("--id", renewalId.Trim());
        }
        else
        {
            args.AddIfPresent("--friendlyname", friendlyName);
        }

        if (force)
        {
            args.Add("--force");
        }

        if (useTestServer)
        {
            args.Add("--test");
            args.Add("--closeonfinish");
        }

        args.Add("--verbose");

        return new WacsCommand(wacsPath, args.ToArray());
    }

    /// <summary>
    /// win-acme takes a comma-separated list of installation plugins but only one
    /// script, so at most two steps are possible: IIS bindings and one script.
    /// </summary>
    private static void AddInstallation(ArgumentList args, string wacsPath, InstallationSettings installation)
    {
        var preset = InstallationPresets.Find(installation.ScriptPresetId) ?? InstallationPresets.None;
        var runsScript = !preset.IsNone;

        var steps = new List<string>();
        if (installation.UpdateIisBindings)
        {
            steps.Add("iis");
        }

        if (runsScript)
        {
            steps.Add("script");
        }

        args.Add("--installation", steps.Count == 0 ? "none" : string.Join(",", steps));

        if (installation.UpdateIisBindings)
        {
            args.AddIfPresent("--installationsiteid", installation.IisSiteId);
            args.AddIfPresent("--sslport", installation.SslPort);
            args.AddIfPresent("--sslipaddress", installation.SslIpAddress);
        }

        if (runsScript)
        {
            args.AddIfPresent("--script", ResolveScriptPath(wacsPath, preset, installation));
            args.AddIfPresent("--scriptparameters", ResolveScriptParameters(preset, installation));
        }
    }

    /// <summary>
    /// Bundled presets live in the Scripts folder next to wacs.exe; the custom preset
    /// uses whatever the user browsed to.
    /// </summary>
    public static string ResolveScriptPath(
        string wacsPath,
        InstallationScriptPreset preset,
        InstallationSettings installation)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(installation);

        if (preset.IsNone)
        {
            return string.Empty;
        }

        if (preset.IsCustom)
        {
            return installation.ScriptPath.Trim();
        }

        var scripts = WacsLocator.ScriptsFolder(wacsPath);
        return scripts.Length == 0 ? preset.ScriptFileName : Path.Combine(scripts, preset.ScriptFileName);
    }

    /// <summary>The user's parameters when they typed any, otherwise the preset's.</summary>
    public static string ResolveScriptParameters(
        InstallationScriptPreset preset,
        InstallationSettings installation)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(installation);

        return string.IsNullOrWhiteSpace(installation.ScriptParameters)
            ? preset.DefaultParameters
            : installation.ScriptParameters.Trim();
    }

    private static void AddValidation(ArgumentList args, ValidationSettings validation)
    {
        var plugin = PluginCatalog.Find(validation.PluginId);
        if (plugin is null)
        {
            return;
        }

        args.Add("--validationmode", plugin.ValidationMode);

        if (plugin.ScriptWiring is { } wiring)
        {
            AddScriptValidation(args, wiring, validation);
            return;
        }

        args.Add("--validation", plugin.Id);

        foreach (var field in plugin.Fields)
        {
            // Credentials that live in a helper's own config file must never reach a
            // command line, so they are skipped here rather than merely left blank.
            if (field.IsExternal)
            {
                continue;
            }

            var value = validation[field.Name].Trim();

            if (field.Kind == PluginFieldKind.Boolean)
            {
                if (bool.TryParse(value, out var enabled) && enabled)
                {
                    args.Add(field.Flag);
                }

                continue;
            }

            if (value.Length > 0)
            {
                args.Add(field.Flag, value, field.IsSecret);
            }
        }
    }

    /// <summary>
    /// Drives an external helper through win-acme's built-in <c>script</c> plugin.
    /// </summary>
    /// <remarks>
    /// <c>--dnsscript</c> is the integrated create-and-delete form, which is right when
    /// one executable handles both and is told which to do by its first argument.
    /// <para>
    /// <c>--dnsscriptparallelism</c> is deliberately left unset, which means serial.
    /// A helper that has no per-record API has to read the whole record set, edit it
    /// and write it back, so two creates running at once would overwrite each other.
    /// </para>
    /// </remarks>
    private static void AddScriptValidation(
        ArgumentList args,
        ScriptWiring wiring,
        ValidationSettings validation)
    {
        args.Add("--validation", "script");

        var executable = validation[wiring.ExecutableFieldName].Trim();
        if (executable.Length == 0)
        {
            return;
        }

        args.Add("--dnsscript", executable);
        args.AddIfPresent("--dnscreatescriptarguments", wiring.CreateArguments);
        args.AddIfPresent("--dnsdeletescriptarguments", wiring.DeleteArguments);
    }

    /// <summary>
    /// Small helper so the builder reads as a list of flags. It refuses to emit a
    /// flag with a blank value, which is the mistake that produces a wacs.exe command
    /// that fails in a confusing way.
    /// </summary>
    private sealed class ArgumentList
    {
        private readonly List<WacsArgument> _arguments = [];

        public void Add(string flag) => _arguments.Add(new WacsArgument(flag));

        public void Add(string flag, string value, bool isSecret = false)
        {
            _arguments.Add(new WacsArgument(flag));
            _arguments.Add(new WacsArgument(value, isSecret));
        }

        public void AddIfPresent(string flag, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Add(flag, value.Trim());
            }
        }

        public WacsArgument[] ToArray() => [.. _arguments];
    }
}
