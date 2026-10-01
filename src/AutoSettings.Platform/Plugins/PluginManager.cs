using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Principal;
using AutoSettings.Core;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Plugins;
using AutoSettings.Core.Updates;

namespace AutoSettings.Platform.Plugins;

/// <summary>A newer version of an installed plugin.</summary>
/// <param name="Plugin">The installed plugin.</param>
/// <param name="Release">The release with the newer version.</param>
public sealed record PluginUpdate(InstalledPlugin Plugin, PluginRelease Release);

/// <summary>
/// Installs, updates and removes plugins in this process. Machine plugins need administrator rights (the app runs
/// itself elevated for them); user plugins do not.
/// </summary>
public sealed class PluginManager : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<ExecutionScope, string> _roots;

    /// <summary>Creates a manager (tests pass their own <see cref="HttpClient"/> and plugin folders).</summary>
    public PluginManager(HttpClient? http = null, Func<ExecutionScope, string>? roots = null)
    {
        _roots = roots ?? RootFor;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Product.Name, AppVersion.Current.ToString()));
    }

    /// <summary>The plugin folder of <paramref name="scope"/>.</summary>
    public static string RootFor(ExecutionScope scope) =>
        scope == ExecutionScope.Machine ? Product.MachinePluginDirectory : Product.UserPluginDirectory;

    /// <summary>Whether this process runs with administrator rights (needed for machine plugins).</summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>The installed plugins of <paramref name="scope"/>.</summary>
    public static List<InstalledPlugin> List(ExecutionScope scope) =>
        PluginStore.Discover(RootFor(scope), scope, AppVersion.Current);

    /// <summary>Installs a package file.</summary>
    /// <exception cref="PluginInstallException">It cannot be installed; the message says why.</exception>
    public PluginManifest InstallFile(string packagePath, ExecutionScope scope)
    {
        if (!File.Exists(packagePath))
            throw new PluginInstallException($"{packagePath} does not exist.");
        return PluginInstaller.Install(_roots(scope), Path.GetFullPath(packagePath), scope, AppVersion.Current, Path.GetFileName(packagePath)).Manifest;
    }

    /// <summary>
    /// Installs the newest package from a GitHub repository's releases. The download must match the release's
    /// <c>SHA256SUMS.txt</c> or the checksum GitHub reports.
    /// </summary>
    /// <param name="repository"><c>owner/repo</c>.</param>
    /// <param name="scope">Machine or user.</param>
    /// <param name="includePrereleases">Whether pre-releases count.</param>
    /// <param name="assetPattern">Which release file is the package.</param>
    /// <param name="expectedId">When updating: the plugin id the package must have.</param>
    /// <param name="newerThan">When updating: only newer versions.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<PluginManifest> InstallFromGitHubAsync(
        string repository,
        ExecutionScope scope,
        bool includePrereleases = false,
        string assetPattern = "*.aspkg",
        string? expectedId = null,
        SemVersion? newerThan = null,
        CancellationToken cancellationToken = default)
    {
        var release = await FindReleaseAsync(repository, assetPattern, includePrereleases, newerThan, cancellationToken).ConfigureAwait(false)
            ?? throw new PluginInstallException(newerThan is null
                ? $"No release of {repository} has a plugin package ({assetPattern})."
                : $"There is no version of {expectedId ?? repository} newer than {newerThan}.");

        var root = _roots(scope);
        var downloads = Path.Combine(root, ".staging");
        Directory.CreateDirectory(downloads);
        var file = Path.Combine(downloads, $"{Guid.NewGuid():N}{PluginPackage.Extension}");
        try
        {
            await DownloadAndVerifyAsync(release, file, cancellationToken).ConfigureAwait(false);
            var (manifest, _) = PluginPackage.Inspect(file);
            if (expectedId is not null && !string.Equals(manifest?.Id, expectedId, StringComparison.Ordinal))
                throw new PluginInstallException($"The package in {repository} is for '{manifest?.Id}', not '{expectedId}'.");
            return PluginInstaller.Install(root, file, scope, AppVersion.Current, $"github:{repository}").Manifest;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Updates a plugin from the GitHub repository named in its manifest.</summary>
    public Task<PluginManifest> UpdateAsync(InstalledPlugin plugin, bool includePrereleases = false, CancellationToken cancellationToken = default)
    {
        var update = plugin.Manifest?.Update ?? throw new PluginInstallException($"{plugin.DisplayName} does not say where its updates come from.");
        return InstallFromGitHubAsync(update.GitHub, plugin.Scope, includePrereleases, update.Asset, plugin.Id,
            SemVersion.TryParse(plugin.Manifest.Version), cancellationToken);
    }

    /// <summary>Looks for newer versions of <paramref name="plugins"/>. Plugins that cannot be checked are skipped.</summary>
    public async Task<List<PluginUpdate>> CheckForUpdatesAsync(IEnumerable<InstalledPlugin> plugins, bool includePrereleases, CancellationToken cancellationToken)
    {
        var updates = new List<PluginUpdate>();
        foreach (var plugin in plugins)
        {
            if (plugin.Manifest is not { Update: { } source } manifest || SemVersion.TryParse(manifest.Version) is not { } current)
                continue;
            try
            {
                if (await FindReleaseAsync(source.GitHub, source.Asset, includePrereleases, current, cancellationToken).ConfigureAwait(false) is { } release)
                    updates.Add(new PluginUpdate(plugin, release));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or PluginInstallException or System.Text.Json.JsonException)
            {
                // Offline or rate limited: try again later.
            }
        }
        return updates;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private async Task<PluginRelease?> FindReleaseAsync(string repository, string assetPattern, bool includePrereleases, SemVersion? newerThan, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PluginReleases.ApiUrl(repository));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new PluginInstallException($"GitHub has no public repository {repository}.");
        if (!response.IsSuccessStatusCode)
            throw new PluginInstallException($"Could not read the releases of {repository} ({(int)response.StatusCode} {response.ReasonPhrase}).");
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return PluginReleases.FindLatest(json, assetPattern, includePrereleases, newerThan);
    }

    private async Task DownloadAndVerifyAsync(PluginRelease release, string file, CancellationToken cancellationToken)
    {
        if (!PluginReleases.IsGitHubDownload(release.AssetUrl) || release.ChecksumsUrl is { } sums && !PluginReleases.IsGitHubDownload(sums))
            throw new PluginInstallException("The download address is not on github.com.");

        string actual;
        await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            using var response = await _http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > PluginPackage.MaxUncompressedBytes)
                    throw new PluginInstallException("The package is too large.");
                sha.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            actual = Convert.ToHexStringLower(sha.GetHashAndReset());
        }

        var expected = new List<string>();
        if (release.ApiSha256 is { } api)
            expected.Add(api);
        if (release.ChecksumsUrl is { } checksumsUrl)
        {
            var text = await _http.GetStringAsync(checksumsUrl, cancellationToken).ConfigureAwait(false);
            if (Checksums.Parse(text).TryGetValue(release.AssetName, out var listed))
                expected.Add(listed);
        }
        if (expected.Count == 0)
            throw new PluginInstallException($"The release {release.Tag} has no checksum for {release.AssetName}: add SHA256SUMS.txt to the release.");
        if (expected.Any(e => !string.Equals(e, actual, StringComparison.OrdinalIgnoreCase)))
            throw new PluginInstallException($"The downloaded {release.AssetName} does not match its published checksum; nothing was installed.");
    }
}
