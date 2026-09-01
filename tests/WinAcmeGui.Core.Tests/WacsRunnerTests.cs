using System.Runtime.InteropServices;
using WinAcmeGui.Core;
using Xunit;

namespace WinAcmeGui.Core.Tests;

/// <summary>
/// Exercises the process plumbing against cmd.exe rather than wacs.exe, so the
/// streaming, exit code and cancellation behaviour is covered without needing a real
/// ACME client. These only do anything on Windows, which is where CI runs.
/// </summary>
public class WacsRunnerTests
{
    private static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static WacsCommand CmdCommand(params string[] arguments) =>
        new("cmd.exe", [.. arguments.Select(a => new WacsArgument(a))]);

    [Fact]
    public async Task CapturesStandardOutputAndAZeroExitCode()
    {
        if (!OnWindows)
        {
            return;
        }

        var streamed = new List<string>();
        var result = await new WacsRunner().RunAsync(
            CmdCommand("/c", "echo hello-from-wacs"),
            streamed.Add);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.OutputLines, line => line.Contains("hello-from-wacs", StringComparison.Ordinal));
        Assert.Contains(streamed, line => line.Contains("hello-from-wacs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsANonZeroExitCodeAsFailure()
    {
        if (!OnWindows)
        {
            return;
        }

        var result = await new WacsRunner().RunAsync(CmdCommand("/c", "exit 3"));

        Assert.False(result.Succeeded);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task CapturesStandardError()
    {
        if (!OnWindows)
        {
            return;
        }

        var result = await new WacsRunner().RunAsync(CmdCommand("/c", "echo oh-no 1>&2"));

        Assert.Contains(result.OutputLines, line => line.Contains("oh-no", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationStopsTheProcess()
    {
        if (!OnWindows)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new WacsRunner().RunAsync(
                CmdCommand("/c", "ping -n 30 127.0.0.1 > nul"),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task AMissingExecutableFailsLoudly()
    {
        var command = new WacsCommand(
            Path.Combine(Path.GetTempPath(), "definitely-not-here-wacs.exe"),
            [new WacsArgument("--version")]);

        await Assert.ThrowsAnyAsync<Exception>(() => new WacsRunner().RunAsync(command));
    }
}
