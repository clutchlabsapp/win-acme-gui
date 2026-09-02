using System.Security.Principal;
using WinAcmeGui.Core;
using WinAcmeGui.Core.Models;
using WinAcmeGui.Core.Plugins;

namespace WinAcmeGui;

internal enum SetupStatus
{
    Pass,
    Warn,
    Fail,
}

internal sealed record SetupCheck(string Name, SetupStatus Status, string Detail);

internal sealed record SetupReport(
    IReadOnlyList<SetupCheck> Checks,
    IReadOnlyList<RenewalSummary> Renewals)
{
    public bool AllPassed => Checks.All(c => c.Status == SetupStatus.Pass);
}

/// <summary>
/// Answers the question the tool exists for: will these certificates actually renew?
/// </summary>
/// <remarks>
/// A working setup needs four things, and the one people miss is the last: win-acme
/// installed, the process elevated, the renewals registered, and a scheduled task to
/// run them. Without the task nothing renews and nothing complains until the
/// certificate expires.
/// </remarks>
internal sealed class SetupChecker(IWacsRunner runner)
{
    private const string ScheduledTaskMarker = "win-acme renew";

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <param name="requiredPlugin">
    /// The DNS plugin this renewal needs, so the report can say whether win-acme can
    /// actually load it. All of them are separate downloads that only work on the
    /// pluggable build.
    /// </param>
    public async Task<SetupReport> RunAsync(
        string wacsPath,
        ValidationPlugin? requiredPlugin = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<SetupCheck>();

        if (string.IsNullOrWhiteSpace(wacsPath) || !File.Exists(wacsPath))
        {
            checks.Add(new SetupCheck(
                "win-acme found",
                SetupStatus.Fail,
                string.IsNullOrWhiteSpace(wacsPath)
                    ? "No path to wacs.exe. Use Browse to point at your win-acme folder."
                    : $"'{wacsPath}' does not exist."));

            return new SetupReport(checks, []);
        }

        checks.Add(new SetupCheck("win-acme found", SetupStatus.Pass, wacsPath));

        var installation = await new WacsInspector(runner)
            .InspectAsync(wacsPath, cancellationToken)
            .ConfigureAwait(false);

        checks.Add(CheckVersion(installation));
        checks.AddRange(CheckPluginAvailability(installation, requiredPlugin));
        checks.Add(CheckElevation());
        checks.Add(await CheckScheduledTaskAsync(cancellationToken).ConfigureAwait(false));

        var (renewalCheck, renewals) = await CheckRenewalsAsync(wacsPath, cancellationToken)
            .ConfigureAwait(false);
        checks.Add(renewalCheck);

        return new SetupReport(checks, renewals);
    }

    private static SetupCheck CheckVersion(WacsInstallation? installation) =>
        installation is null
            ? new SetupCheck("win-acme runs", SetupStatus.Fail, "wacs.exe did not run or did not report a version.")
            : new SetupCheck("win-acme runs", SetupStatus.Pass, installation.Description);

    /// <summary>
    /// Every DNS provider this tool offers is a separate win-acme download, and none
    /// of them load into the trimmed build. Both failures are silent at renewal time,
    /// so they are worth reporting here.
    /// </summary>
    private static IEnumerable<SetupCheck> CheckPluginAvailability(
        WacsInstallation? installation,
        ValidationPlugin? requiredPlugin)
    {
        if (installation is null || requiredPlugin is null)
        {
            yield break;
        }

        if (!installation.SupportsPlugins)
        {
            yield return new SetupCheck(
                "DNS plugin",
                SetupStatus.Fail,
                $"This is the trimmed build, which cannot load the {requiredPlugin.DisplayName} plugin. "
                + "Install the pluggable build instead.");

            yield break;
        }

        yield return installation.HasPlugin(requiredPlugin.Id)
            ? new SetupCheck("DNS plugin", SetupStatus.Pass, $"{requiredPlugin.DisplayName} plugin is loaded.")
            : new SetupCheck(
                "DNS plugin",
                SetupStatus.Fail,
                $"The {requiredPlugin.DisplayName} plugin is not installed. It is a separate download; "
                + "use Download and install to add it.");
    }

    private static SetupCheck CheckElevation() =>
        IsElevated
            ? new SetupCheck("Running as administrator", SetupStatus.Pass, "Elevated.")
            : new SetupCheck(
                "Running as administrator",
                SetupStatus.Fail,
                "win-acme cannot write to the machine certificate store or the task scheduler "
                + "without elevation. Restart this tool as administrator.");

    private async Task<SetupCheck> CheckScheduledTaskAsync(CancellationToken cancellationToken)
    {
        try
        {
            // schtasks avoids taking a dependency on the TaskScheduler library for one lookup.
            var query = new WacsCommand(
                "schtasks.exe",
                [new WacsArgument("/query"), new WacsArgument("/fo"), new WacsArgument("csv"), new WacsArgument("/nh")]);

            var result = await runner.RunAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);

            var task = result.OutputLines.FirstOrDefault(
                line => line.Contains(ScheduledTaskMarker, StringComparison.OrdinalIgnoreCase));

            if (task is null)
            {
                return new SetupCheck(
                    "Scheduled task",
                    SetupStatus.Fail,
                    "No 'win-acme renew' task found. Nothing will renew automatically. "
                    + "Run wacs.exe --setuptaskscheduler to create it.");
            }

            return task.Contains("Disabled", StringComparison.OrdinalIgnoreCase)
                ? new SetupCheck("Scheduled task", SetupStatus.Fail, $"The task exists but is disabled: {task.Trim()}")
                : new SetupCheck("Scheduled task", SetupStatus.Pass, task.Trim());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SetupCheck("Scheduled task", SetupStatus.Warn, $"Could not query the task scheduler: {exception.Message}");
        }
    }

    private async Task<(SetupCheck Check, IReadOnlyList<RenewalSummary> Renewals)> CheckRenewalsAsync(
        string wacsPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner
                .RunAsync(WacsArgumentBuilder.BuildList(wacsPath), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var renewals = RenewalListParser.Parse(result.Output);

            if (renewals.Count == 0)
            {
                return (new SetupCheck(
                    "Renewals",
                    SetupStatus.Warn,
                    RenewalListParser.IsEmptyList(result.Output)
                        ? "No renewals are configured yet."
                        : "Could not read the renewal list from wacs.exe output."), renewals);
            }

            var failing = renewals.Count(r => r.HasErrors);
            var detail = $"{renewals.Count} renewal{(renewals.Count == 1 ? string.Empty : "s")} configured"
                         + (failing > 0 ? $", {failing} with errors" : string.Empty);

            return (new SetupCheck("Renewals", failing > 0 ? SetupStatus.Warn : SetupStatus.Pass, detail), renewals);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (new SetupCheck("Renewals", SetupStatus.Fail, exception.Message),
                Array.Empty<RenewalSummary>());
        }
    }
}
