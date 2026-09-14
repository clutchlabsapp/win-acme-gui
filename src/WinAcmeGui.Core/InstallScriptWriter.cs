using System.Reflection;
using System.Text;

namespace WinAcmeGui.Core;

/// <summary>
/// Puts the post-renewal scripts this tool ships onto disk, next to wacs.exe, so
/// win-acme can run them.
/// </summary>
/// <remarks>
/// The scripts live in the repo under <c>scripts/</c> and are embedded into this
/// assembly, so there is exactly one copy to review and no risk of the file on disk
/// drifting from the source.
/// </remarks>
public static class InstallScriptWriter
{
    /// <summary>The RDP listener script's file name once written out.</summary>
    public const string RdpListenerScriptName = "Set-RdpListenerCertificate.ps1";

    private const string ResourcePrefix = "WinAcmeGui.Core.Scripts.";

    /// <summary>Reads a shipped script out of the assembly.</summary>
    /// <exception cref="InvalidOperationException">The build did not embed it.</exception>
    public static string ReadScript(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var resourceName = ResourcePrefix + fileName;
        using var stream = typeof(InstallScriptWriter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"'{resourceName}' is not embedded in {typeof(InstallScriptWriter).Assembly.GetName().Name}.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Writes the script into <paramref name="folder"/> and returns its full path.
    /// An identical file is left alone, so the timestamp only moves when the content
    /// actually changed.
    /// </summary>
    public static string WriteScript(string folder, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var content = ReadScript(fileName);
        var path = Path.Combine(folder, fileName);

        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return path;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return path;
    }

    /// <summary>True when the file on disk already matches what is embedded.</summary>
    public static bool IsUpToDate(string folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var path = Path.Combine(folder, fileName);

        try
        {
            return File.Exists(path)
                   && string.Equals(File.ReadAllText(path), ReadScript(fileName), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
