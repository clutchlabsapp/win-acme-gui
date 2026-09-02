using System.Text.Json;

namespace WinAcmeGui.Core;

/// <summary>One file attached to a win-acme GitHub release.</summary>
public sealed record WinAcmeAsset(string Name, Uri DownloadUrl, long SizeBytes)
{
    public string SizeDescription => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / (1024.0 * 1024.0):0.#} MB"
        : $"{Math.Max(1, SizeBytes / 1024)} KB";
}

/// <summary>
/// A win-acme release and the files attached to it.
/// </summary>
/// <remarks>
/// Assets follow a fixed naming scheme, for example at v2.2.9.1701:
/// <code>
/// win-acme.v2.2.9.1701.x64.pluggable.zip
/// win-acme.v2.2.9.1701.x64.trimmed.zip
/// plugin.validation.dns.cloudflare.v2.2.9.1701.zip
/// </code>
/// The DNS validation plugins this tool offers are all separate downloads, and they
/// only load into the <c>pluggable</c> build — the smaller <c>trimmed</c> build is
/// IL-trimmed and cannot load external plugins at all.
/// </remarks>
public sealed record WinAcmeRelease(string TagName, string Version, IReadOnlyList<WinAcmeAsset> Assets)
{
    /// <summary>Hosts a download is allowed to come from. Anything else is refused.</summary>
    private static readonly string[] TrustedHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    ];

    /// <summary>
    /// The main program. Always the pluggable build: the trimmed one cannot load the
    /// DNS plugins this tool is built around.
    /// </summary>
    public WinAcmeAsset? MainPackage(string architecture, bool pluggable = true)
    {
        var variant = pluggable ? "pluggable" : "trimmed";
        var wanted = $"win-acme.v{Version}.{architecture}.{variant}.zip";

        return Assets.FirstOrDefault(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase))
               ?? Assets.FirstOrDefault(a =>
                   a.Name.StartsWith("win-acme.", StringComparison.OrdinalIgnoreCase)
                   && a.Name.Contains($".{architecture}.", StringComparison.OrdinalIgnoreCase)
                   && a.Name.Contains($".{variant}.", StringComparison.OrdinalIgnoreCase)
                   && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The download for one DNS validation plugin, by its win-acme plugin id.</summary>
    public WinAcmeAsset? DnsPlugin(string pluginId)
    {
        var prefix = $"plugin.validation.dns.{pluginId}.";

        return Assets.FirstOrDefault(a =>
            a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsTrustedDownload(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && TrustedHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the payload of the GitHub "latest release" API.</summary>
    public static WinAcmeRelease Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagName)
            ? tagName.GetString() ?? string.Empty
            : string.Empty;

        var assets = new List<WinAcmeAsset>();

        if (root.TryGetProperty("assets", out var assetArray) && assetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetArray.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

                if (string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(url)
                    || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                {
                    continue;
                }

                var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
                assets.Add(new WinAcmeAsset(name, parsed, size));
            }
        }

        return new WinAcmeRelease(tag, tag.TrimStart('v', 'V'), assets);
    }
}
