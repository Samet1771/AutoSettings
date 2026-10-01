using System.Net;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Plugins;
using AutoSettings.Core.Updates;
using AutoSettings.Platform.Plugins;

namespace AutoSettings.Platform.Tests;

public sealed class PluginManagerTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("autosettings-manager-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Answers GitHub's release list and downloads from memory.</summary>
    private sealed class FakeGitHub(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var key = files.Keys.FirstOrDefault(k => url.StartsWith(k, StringComparison.Ordinal));
            return Task.FromResult(key is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(files[key]) });
        }
    }

    private string Package()
    {
        var folder = Path.Combine(_temp, "build");
        CopyDirectory(Path.Combine(ScriptPluginTests.RepositoryRoot(), "samples", "plugins", "hello-script", "1.0.0"), folder);
        var package = Path.Combine(_temp, "example.hello-1.0.0.aspkg");
        PluginPackage.Create(folder, File.ReadAllText(Path.Combine(folder, "plugin.yaml")), package);
        return package;
    }

    private PluginManager Manager(byte[] package, string checksumsLine)
    {
        const string download = "https://github.com/example/hello/releases/download/v1.0.0/";
        var releases = $$"""
            [{"tag_name":"v1.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/example/hello/releases/tag/v1.0.0",
              "assets":[{"name":"example.hello-1.0.0.aspkg","browser_download_url":"{{download}}example.hello-1.0.0.aspkg"},
                        {"name":"SHA256SUMS.txt","browser_download_url":"{{download}}SHA256SUMS.txt"}]}]
            """;
        var http = new HttpClient(new FakeGitHub(new Dictionary<string, byte[]>
        {
            [PluginReleases.ApiUrl("example/hello")] = System.Text.Encoding.UTF8.GetBytes(releases),
            [download + "example.hello-1.0.0.aspkg"] = package,
            [download + "SHA256SUMS.txt"] = System.Text.Encoding.UTF8.GetBytes(checksumsLine),
        }));
        return new PluginManager(http, scope => Path.Combine(_temp, scope.ToString()));
    }

    [Fact]
    public async Task Installs_a_verified_package_from_GitHub()
    {
        var package = Package();
        using var manager = Manager(File.ReadAllBytes(package), Checksums.FormatLine(PluginPackage.Sha256(package), "example.hello-1.0.0.aspkg"));

        var manifest = await manager.InstallFromGitHubAsync("example/hello", ExecutionScope.User);

        Assert.Equal("example.hello", manifest.Id);
        var installed = Assert.Single(PluginStore.Discover(Path.Combine(_temp, "User"), ExecutionScope.User));
        Assert.True(installed.IsActive, string.Join("\n", installed.Issues));
        Assert.Equal("github:example/hello", InstalledPluginsFile.Read(Path.Combine(_temp, "User")).Find("example.hello")!.Source);
    }

    [Fact]
    public async Task Refuses_a_package_that_does_not_match_its_checksum()
    {
        var package = Package();
        var tampered = File.ReadAllBytes(package);
        tampered[^10] ^= 0xFF;
        using var manager = Manager(tampered, Checksums.FormatLine(PluginPackage.Sha256(package), "example.hello-1.0.0.aspkg"));

        var error = await Assert.ThrowsAsync<PluginInstallException>(() => manager.InstallFromGitHubAsync("example/hello", ExecutionScope.User));

        Assert.Contains("does not match", error.Message);
        Assert.Empty(PluginStore.Discover(Path.Combine(_temp, "User"), ExecutionScope.User));
    }

    [Fact]
    public async Task Refuses_a_release_without_checksums_and_finds_updates()
    {
        var package = Package();
        using var manager = Manager(File.ReadAllBytes(package), "");
        var error = await Assert.ThrowsAsync<PluginInstallException>(() => manager.InstallFromGitHubAsync("example/hello", ExecutionScope.User));
        Assert.Contains("no checksum", error.Message);

        var missing = await Assert.ThrowsAsync<PluginInstallException>(() => manager.InstallFromGitHubAsync("example/none", ExecutionScope.User));
        Assert.Contains("no public repository", missing.Message);

        // A plugin at 0.9.0 that updates from example/hello sees 1.0.0.
        var old = new InstalledPlugin
        {
            Id = "example.hello",
            Directory = _temp,
            Scope = ExecutionScope.User,
            Manifest = new PluginManifest { Id = "example.hello", Version = "0.9.0", Update = new PluginUpdateSource("example/hello") },
        };
        var update = Assert.Single(await manager.CheckForUpdatesAsync([old], includePrereleases: false, CancellationToken.None));
        Assert.Equal("1.0.0", update.Release.Version.ToString());
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
