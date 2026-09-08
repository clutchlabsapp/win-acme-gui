using System.Text.RegularExpressions;
using WinAcmeGui.Core.Plugins;

namespace WinAcmeGui.Core;

/// <summary>What is actually installed at a given wacs.exe path.</summary>
public sealed record WacsInstallation(string Path, string Version, bool IsTrimmed)
{
    /// <summary>win-acme plugin ids whose command line arguments wacs.exe reports.</summary>
    public IReadOnlyCollection<string> AvailableValidationPlugins { get; init; } = [];

    /// <summary>
    /// The trimmed build cannot load external plugins, and every DNS provider this
    /// tool offers is an external plugin.
    /// </summary>
    public bool SupportsPlugins => !IsTrimmed;

    public bool HasPlugin(string pluginId) =>
        AvailableValidationPlugins.Contains(pluginId, StringComparer.OrdinalIgnoreCase);

    public string Description =>
        $"win-acme {Version} ({(IsTrimmed ? "trimmed" : "pluggable")})";
}

/// <summary>
/// Asks an installed wacs.exe what it is and what it can do, rather than guessing
/// from file names on disk.
/// </summary>
public sealed partial class WacsInspector(IWacsRunner runner)
{
    /// <summary>
    /// Returns null when there is no usable wacs.exe at <paramref name="wacsPath"/>.
    /// </summary>
    public async Task<WacsInstallation?> InspectAsync(
        string? wacsPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(wacsPath) || !File.Exists(wacsPath))
        {
            return null;
        }

        WacsResult version;

        try
        {
            version = await runner
                .RunAsync(WacsArgumentBuilder.BuildVersion(wacsPath), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        var installation = ReadVersion(wacsPath, version.Output);

        if (installation is null || installation.IsTrimmed)
        {
            // No point asking a trimmed build which plugins it has; it can load none.
            return installation;
        }

        var plugins = await ReadAvailablePluginsAsync(wacsPath, cancellationToken).ConfigureAwait(false);
        return installation with { AvailableValidationPlugins = plugins };
    }

    /// <summary>
    /// win-acme prints a line such as
    /// <c>Software version 2.2.9.1701 (release, trimmed, standalone, 64-bit)</c>,
    /// which names both the version and the build variant.
    /// </summary>
    public static WacsInstallation? ReadVersion(string wacsPath, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = VersionPattern().Match(output);

        return match.Success
            ? new WacsInstallation(
                wacsPath,
                match.Groups["version"].Value,
                output.Contains("trimmed", StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>
    /// wacs.exe --help lists every loaded plugin with the condition that selects it,
    /// for example <c>[--validation godaddy]</c>. Finding that line is proof win-acme
    /// can actually use the plugin.
    /// </summary>
    public static IReadOnlyCollection<string> ReadAvailablePlugins(string? helpOutput)
    {
        if (string.IsNullOrWhiteSpace(helpOutput))
        {
            return [];
        }

        return PluginCatalog.ValidationPlugins
            .Where(plugin => helpOutput.Contains(plugin.HelpCondition, StringComparison.OrdinalIgnoreCase))
            .Select(plugin => plugin.Id)
            .ToArray();
    }

    private async Task<IReadOnlyCollection<string>> ReadAvailablePluginsAsync(
        string wacsPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var help = await runner
                .RunAsync(WacsArgumentBuilder.BuildHelp(wacsPath), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ReadAvailablePlugins(help.Output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"version\s+(?<version>\d+(\.\d+)+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
}
