using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace WinAcmeGui.Core;

/// <summary>
/// Downloads win-acme from its official GitHub releases and unpacks it, so the user
/// does not have to go and find it themselves.
/// </summary>
/// <remarks>
/// Two details make this worth automating rather than leaving as a manual step:
/// the DNS validation plugins are separate downloads that must be unpacked into the
/// same folder as wacs.exe, and they only work on the <c>pluggable</c> build. Getting
/// either wrong produces a win-acme that simply does not offer your DNS provider.
/// <para>
/// Unpacking here also sidesteps the "unblock the .dll files" step in win-acme's own
/// instructions: the mark-of-the-web that makes .NET distrust the plugin DLLs is
/// applied by browsers, not by a program writing files itself.
/// </para>
/// </remarks>
public sealed class WinAcmeInstaller(HttpClient? httpClient = null)
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/win-acme/win-acme/releases/latest";

    public const string ProjectUrl = "https://www.win-acme.com/";
    public const string ReleasesUrl = "https://github.com/win-acme/win-acme/releases";

    private readonly HttpClient _http = httpClient ?? CreateDefaultClient();

    /// <summary>Where win-acme's own documentation recommends unpacking to.</summary>
    public static string DefaultInstallFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "win-acme");

    /// <summary>The win-acme build matching this machine.</summary>
    public static string CurrentArchitecture => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x64",
    };

    public async Task<WinAcmeRelease> FetchLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not read the win-acme release list from GitHub ({(int)response.StatusCode} "
                + $"{response.ReasonPhrase}). You can download it manually from {ReleasesUrl}.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var release = WinAcmeRelease.Parse(json);

        return release.Assets.Count == 0
            ? throw new InvalidOperationException("GitHub returned no downloads for the latest win-acme release.")
            : release;
    }

    /// <summary>
    /// Downloads and unpacks win-acme plus the requested DNS plugins into
    /// <paramref name="targetFolder"/>, and returns the path to wacs.exe.
    /// </summary>
    /// <param name="dnsPluginIds">win-acme plugin ids, for example <c>cloudflare</c>.</param>
    public async Task<string> InstallAsync(
        WinAcmeRelease release,
        string targetFolder,
        IEnumerable<string> dnsPluginIds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        var main = release.MainPackage(CurrentArchitecture)
            ?? throw new InvalidOperationException(
                $"Release {release.TagName} has no {CurrentArchitecture} pluggable build.");

        Directory.CreateDirectory(targetFolder);

        await DownloadAndExtractAsync(main, targetFolder, progress, cancellationToken).ConfigureAwait(false);

        foreach (var pluginId in dnsPluginIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var plugin = release.DnsPlugin(pluginId);

            if (plugin is null)
            {
                progress?.Report($"No separate download exists for the {pluginId} plugin; skipping it.");
                continue;
            }

            await DownloadAndExtractAsync(plugin, targetFolder, progress, cancellationToken).ConfigureAwait(false);
        }

        var wacsPath = Path.Combine(targetFolder, WacsLocator.ExecutableName);

        return File.Exists(wacsPath)
            ? wacsPath
            : throw new InvalidOperationException(
                $"The download unpacked but {WacsLocator.ExecutableName} is not in {targetFolder}.");
    }

    private async Task DownloadAndExtractAsync(
        WinAcmeAsset asset,
        string targetFolder,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!WinAcmeRelease.IsTrustedDownload(asset.DownloadUrl))
        {
            throw new InvalidOperationException(
                $"Refusing to download {asset.Name} from {asset.DownloadUrl.Host}: not a GitHub release host.");
        }

        progress?.Report($"Downloading {asset.Name} ({asset.SizeDescription})...");

        var temporaryFile = Path.Combine(Path.GetTempPath(), $"win-acme-gui-{Guid.NewGuid():N}.zip");

        try
        {
            using (var response = await _http
                       .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var destination = File.Create(temporaryFile);

                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report($"Unpacking {asset.Name} into {targetFolder}...");
            ExtractSafely(temporaryFile, targetFolder);
        }
        finally
        {
            TryDelete(temporaryFile);
        }
    }

    /// <summary>
    /// Extracts a zip, refusing any entry that would land outside the target folder.
    /// Existing files are overwritten, which is what makes this double as a repair or
    /// upgrade rather than only a first install.
    /// </summary>
    public static void ExtractSafely(string zipPath, string targetFolder)
    {
        var root = Path.GetFullPath(targetFolder);
        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));

            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destination, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The archive contains an entry that would be written outside the target folder: {entry.FullName}");
            }

            // A directory entry has an empty name.
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover file in the temp folder is not worth failing an install over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        // The GitHub API rejects requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("win-acme-gui", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return client;
    }
}
