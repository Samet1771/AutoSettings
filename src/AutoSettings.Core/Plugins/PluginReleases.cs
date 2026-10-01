using System.Text.Json;
using System.Text.RegularExpressions;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Plugins;

/// <summary>A plugin package published in a GitHub release.</summary>
/// <param name="Version">The release version (from its tag).</param>
/// <param name="Tag">The release tag.</param>
/// <param name="IsPrerelease">Whether it is a pre-release.</param>
/// <param name="PageUrl">The release page.</param>
/// <param name="AssetName">The package file name.</param>
/// <param name="AssetUrl">Where to download the package.</param>
/// <param name="ChecksumsUrl">Where to download <c>SHA256SUMS.txt</c>, if the release has one.</param>
/// <param name="ApiSha256">The SHA-256 GitHub reports for the package, if any.</param>
public sealed record PluginRelease(
    SemVersion Version, string Tag, bool IsPrerelease, string PageUrl, string AssetName, string AssetUrl, string? ChecksumsUrl, string? ApiSha256);

/// <summary>Finds plugin packages in GitHub releases, like the app's own updates.</summary>
public static partial class PluginReleases
{
    /// <summary>The GitHub API address of a repository's releases.</summary>
    public static string ApiUrl(string repository) => $"https://api.github.com/repos/{repository}/releases?per_page=20";

    /// <summary>
    /// Reads where to install from: <c>owner/repo</c>, <c>github:owner/repo</c> or a github.com link to the repository
    /// or one of its releases. Returns false for anything else.
    /// </summary>
    public static bool TryParseRepository(string? text, out string repository)
    {
        repository = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var match = RepositoryPattern().Match(text.Trim());
        if (!match.Success)
            return false;
        repository = $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}";
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repository = repository[..^4];
        return true;
    }

    /// <summary>
    /// The newest release with a package matching <paramref name="assetPattern"/>, skipping drafts and, unless
    /// <paramref name="includePrereleases"/>, pre-releases. With <paramref name="newerThan"/>, only newer releases count.
    /// </summary>
    public static PluginRelease? FindLatest(string json, string assetPattern = "*.aspkg", bool includePrereleases = false, SemVersion? newerThan = null)
    {
        PluginRelease? best = null;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (Bool(element, "draft"))
                continue;
            var tag = Text(element, "tag_name");
            if (SemVersion.TryParse(tag) is not { } version)
                continue;
            var prerelease = Bool(element, "prerelease") || version.IsPrerelease;
            if (prerelease && !includePrereleases || newerThan is not null && version <= newerThan)
                continue;
            if (best is not null && version <= best.Version)
                continue;

            string? assetName = null, assetUrl = null, checksums = null, sha = null;
            if (element.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Text(asset, "name");
                    var url = Text(asset, "browser_download_url");
                    if (name is null || url is null)
                        continue;
                    if (assetUrl is null && Wildcard.IsMatch(assetPattern, name))
                    {
                        assetName = name;
                        assetUrl = url;
                        sha = ReleaseFeed.ParseDigest(Text(asset, "digest"));
                    }
                    else if (name.Equals(ReleaseFeed.ChecksumsFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        checksums = url;
                    }
                }
            }
            if (assetName is null || assetUrl is null)
                continue;
            best = new PluginRelease(version, tag!, prerelease, Text(element, "html_url") ?? "", assetName, assetUrl, checksums, sha);
        }
        return best;
    }

    /// <summary>Whether <paramref name="url"/> is an HTTPS download from GitHub.</summary>
    public static bool IsGitHubDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^(?:github:|https://github\.com/)?(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<repo>[A-Za-z0-9._-]{1,100})(?:/(?:releases(?:/.*)?)?)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
}
