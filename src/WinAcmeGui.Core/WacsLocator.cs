namespace WinAcmeGui.Core;

/// <summary>
/// Finds wacs.exe so the user usually does not have to browse for it.
/// </summary>
public static class WacsLocator
{
    public const string ExecutableName = "wacs.exe";

    /// <summary>
    /// The places win-acme is normally unpacked to, in the order they are tried.
    /// win-acme ships as a zip with no installer, so there is no registry key to read;
    /// these are the conventional locations plus whatever is on PATH.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths()
    {
        var roots = new List<string>();

        AddFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "win-acme");
        AddFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "win-acme");
        AddFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "win-acme");
        AddFolder(roots, "C:\\win-acme");
        AddFolder(roots, "C:\\tools\\win-acme");

        foreach (var pathEntry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            AddFolder(roots, pathEntry.Trim());
        }

        return roots;
    }

    /// <summary>
    /// Returns the first wacs.exe that exists, preferring <paramref name="preferredPath"/>
    /// — normally the path remembered from last time — or null when none is found.
    /// </summary>
    /// <param name="fileExists">Injected so this can be tested off Windows.</param>
    public static string? Locate(string? preferredPath = null, Func<string, bool>? fileExists = null)
    {
        var exists = fileExists ?? File.Exists;

        if (!string.IsNullOrWhiteSpace(preferredPath) && exists(preferredPath))
        {
            return preferredPath;
        }

        return CandidatePaths().FirstOrDefault(exists);
    }

    private static void AddFolder(List<string> roots, string? folder, string? subFolder = null)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var directory = subFolder is null ? folder : Path.Combine(folder, subFolder);
        var candidate = Path.Combine(directory, ExecutableName);

        if (!roots.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(candidate);
        }
    }

    /// <summary>
    /// The Scripts folder that ships alongside wacs.exe, which holds the RDP and
    /// Exchange import scripts used by the phase 2 installation presets.
    /// </summary>
    public static string ScriptsFolder(string wacsPath)
    {
        var directory = Path.GetDirectoryName(wacsPath);
        return string.IsNullOrEmpty(directory) ? string.Empty : Path.Combine(directory, "Scripts");
    }
}
