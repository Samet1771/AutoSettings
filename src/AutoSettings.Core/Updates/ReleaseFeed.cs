using System.Text.Json;

namespace AutoSettings.Core.Updates;

/// <summary>Reads the GitHub releases list (<c>GET /repos/{owner}/{repo}/releases</c>) and picks the update to offer.</summary>
public static class ReleaseFeed
{
    /// <summary>Name of the checksum file attached to every release.</summary>
    public const string ChecksumsFileName = "SHA256SUMS.txt";

    /// <summary>
    /// Returns the newest release that is newer than <paramref name="current"/> and has an MSI installer,
    /// skipping drafts and, unless <paramref name="includePrereleases"/> is set, pre-releases. Null when there is none.
    /// </summary>
    public static ReleaseInfo? FindUpdate(string json, SemVersion current, bool includePrereleases)
    {
        ReleaseInfo? best = null;
        SemVersion? bestVersion = null;
        foreach (var (release, version) in Parse(json))
        {
            if (release.IsPrerelease && !includePrereleases)
                continue;
            if (version <= current)
                continue;
            if (bestVersion is null || version > bestVersion)
            {
                best = release;
                bestVersion = version;
            }
        }
        return best;
    }

    /// <summary>Parses every published release that has a version tag and an MSI asset.</summary>
    public static IEnumerable<(ReleaseInfo Release, SemVersion Version)> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            yield break;

        var releases = new List<(ReleaseInfo, SemVersion)>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (GetBool(element, "draft"))
                continue;
            var tag = GetString(element, "tag_name");
            var version = SemVersion.TryParse(tag);
            if (tag is null || version is null)
                continue;

            string? msiUrl = null, msiName = null, checksumsUrl = null, apiSha = null;
            if (element.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = GetString(asset, "name");
                    var url = GetString(asset, "browser_download_url");
                    if (name is null || url is null)
                        continue;
                    if (IsInstaller(name) && msiUrl is null)
                    {
                        msiUrl = url;
                        msiName = name;
                        apiSha = ParseDigest(GetString(asset, "digest"));
                    }
                    else if (name.Equals(ChecksumsFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        checksumsUrl = url;
                    }
                }
            }
            if (msiUrl is null || msiName is null)
                continue;

            var info = new ReleaseInfo(
                version.ToString(),
                tag,
                GetBool(element, "prerelease") || version.IsPrerelease,
                GetString(element, "html_url") ?? "",
                GetString(element, "body") ?? "",
                msiUrl,
                msiName,
                checksumsUrl,
                apiSha);
            releases.Add((info, version));
        }

        foreach (var release in releases)
            yield return release;
    }

    /// <summary>True for the x64 MSI of a release, for example <c>AutoSettings-0.3.0-x64.msi</c>.</summary>
    public static bool IsInstaller(string fileName) =>
        fileName.StartsWith(Product.Name, StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith("-x64.msi", StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns a GitHub asset digest (<c>sha256:ABC…</c>) into lowercase hex, or null.</summary>
    public static string? ParseDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var hex = digest[prefix.Length..].Trim().ToLowerInvariant();
        return Checksums.IsSha256(hex) ? hex : null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>Reads checksum files in the <c>sha256sum</c> format: <c>&lt;hex&gt;  &lt;file name&gt;</c> per line.</summary>
public static class Checksums
{
    /// <summary>Returns file name → lowercase SHA-256 hex. Invalid lines are ignored.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var space = line.IndexOfAny([' ', '\t']);
            if (space <= 0)
                continue;
            var hash = line[..space].ToLowerInvariant();
            // "*name" marks binary mode in sha256sum output.
            var name = line[space..].Trim().TrimStart('*');
            if (IsSha256(hash) && name.Length > 0)
                result[name] = hash;
        }
        return result;
    }

    /// <summary>True for 64 hexadecimal characters.</summary>
    public static bool IsSha256(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);

    /// <summary>Formats a checksum file line.</summary>
    public static string FormatLine(string sha256, string fileName) => $"{sha256.ToLowerInvariant()}  {fileName}";
}
