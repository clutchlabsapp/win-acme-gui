using System.Diagnostics;

namespace WinAcmeGui.Core;

/// <summary>What a wacs.exe run produced.</summary>
public sealed record WacsResult(int ExitCode, IReadOnlyList<string> OutputLines)
{
    public bool Succeeded => ExitCode == 0;

    public string Output => string.Join(Environment.NewLine, OutputLines);
}

/// <summary>
/// Runs wacs.exe. Behind an interface so the UI and the setup checks can be tested
/// with a fake instead of a real ACME client.
/// </summary>
public interface IWacsRunner
{
    Task<WacsResult> RunAsync(
        WacsCommand command,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WacsRunner : IWacsRunner
{
    public async Task<WacsResult> RunAsync(
        WacsCommand command,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            // ArgumentList quotes each argument for us. Building one string by hand is
            // how credentials containing spaces or quotes end up mangled.
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(command.ExecutablePath) ?? string.Empty,
        };

        foreach (var value in command.ArgumentValues)
        {
            startInfo.ArgumentList.Add(value);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var lines = new List<string>();
        var sync = new object();

        void Capture(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (sync)
            {
                lines.Add(line);
            }

            onOutput?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {command.ExecutablePath}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        lock (sync)
        {
            return new WacsResult(process.ExitCode, lines.ToArray());
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process finished between the check and the kill. Nothing to do.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied while tearing down; the run is being abandoned anyway.
        }
    }
}
