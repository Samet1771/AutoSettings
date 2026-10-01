using AutoSettings.Core.Catalog;
using AutoSettings.Core.Plugins;
using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Tests;

public sealed class PluginInstallerTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("autosettings-install-").FullName;
    private string Root => Path.Combine(_temp, "plugins");

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

    /// <summary>Packs the test script plugin (acme.usb) at <paramref name="version"/>.</summary>
    private string Package(string version, string? yaml = null)
    {
        var folder = Path.Combine(_temp, "build-" + version);
        Directory.CreateDirectory(folder);
        foreach (var script in new[] { "poll.ps1", "present.ps1", "label/set.ps1", "label/get.ps1" })
        {
            var path = Path.Combine(folder, script);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "# " + version);
        }
        yaml ??= PluginManifestTests.UsbScript.Replace("version: 1.2.0", $"version: {version}");
        var package = Path.Combine(_temp, $"acme.usb-{version}.aspkg");
        PluginPackage.Create(folder, yaml, package);
        return package;
    }

    [Fact]
    public void Install_update_and_rollback()
    {
        var (manifest, entry) = PluginInstaller.Install(Root, Package("1.0.0"), ExecutionScope.User, SemVersion.Parse("0.3.0"));
        Assert.Equal("acme.usb", manifest.Id);
        Assert.Equal("1.0.0", entry.Version);
        Assert.Null(entry.Previous);
        Assert.Equal("acme.usb-1.0.0.aspkg", entry.Source);
        Assert.Equal("1.0.0", Assert.Single(PluginStore.Discover(Root, ExecutionScope.User)).Manifest!.Version);

        (_, entry) = PluginInstaller.Install(Root, Package("1.1.0"), ExecutionScope.User, null, "github:acme/usb");
        Assert.Equal("1.1.0", entry.Version);
        Assert.Equal("1.0.0", entry.Previous);
        Assert.Equal("github:acme/usb", entry.Source);
        Assert.Equal("1.1.0", Assert.Single(PluginStore.Discover(Root, ExecutionScope.User)).Manifest!.Version);

        // A third version keeps only the one before it.
        PluginInstaller.Install(Root, Package("1.2.0"), ExecutionScope.User, null);
        PluginInstaller.CleanUp(Root);
        Assert.False(Directory.Exists(Path.Combine(Root, "acme.usb", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(Root, "acme.usb", "1.1.0")));

        Assert.Equal("1.1.0", PluginInstaller.Rollback(Root, "acme.usb"));
        Assert.Equal("1.1.0", Assert.Single(PluginStore.Discover(Root, ExecutionScope.User)).Manifest!.Version);
        Assert.Equal("1.2.0", PluginInstaller.Rollback(Root, "acme.usb"));
    }

    [Fact]
    public void Plugins_can_be_turned_off_and_removed()
    {
        PluginInstaller.Install(Root, Package("1.0.0"), ExecutionScope.User, null);

        PluginInstaller.SetEnabled(Root, "acme.usb", false);
        Assert.False(Assert.Single(PluginStore.Discover(Root, ExecutionScope.User)).Enabled);
        PluginInstaller.SetEnabled(Root, "acme.usb", true);
        Assert.True(Assert.Single(PluginStore.Discover(Root, ExecutionScope.User)).Enabled);

        Assert.True(PluginInstaller.Uninstall(Root, "acme.usb"));
        Assert.Empty(PluginStore.Discover(Root, ExecutionScope.User));
        Assert.False(PluginInstaller.Uninstall(Root, "acme.usb"));
        Assert.False(PluginInstaller.Uninstall(Root, "../outside"));
    }

    [Fact]
    public void Plugins_copied_in_by_hand_can_be_turned_off()
    {
        PluginPackage.Extract(Package("1.0.0"), Path.Combine(Root, "acme.usb", "1.0.0"));

        PluginInstaller.SetEnabled(Root, "acme.usb", false);

        Assert.False(Assert.Single(PluginStore.Discover(Root, ExecutionScope.User)).Enabled);
        Assert.Throws<PluginInstallException>(() => PluginInstaller.SetEnabled(Root, "acme.none", false));
    }

    [Fact]
    public void Invalid_or_wrong_scope_packages_are_refused()
    {
        var tooNew = Package("1.0.0", PluginManifestTests.UsbScript.Replace("version: 1.2.0", "version: 1.0.0").Replace("min_app_version: 0.3.0", "min_app_version: 9.0.0"));
        var error = Assert.Throws<PluginInstallException>(() => PluginInstaller.Install(Root, tooNew, ExecutionScope.User, SemVersion.Parse("0.3.0")));
        Assert.Contains("9.0.0", error.Message);

        var machine = Package("2.0.0", PluginManifestTests.UsbScript.Replace("version: 1.2.0", "version: 2.0.0").Replace("scope: user", "scope: machine"));
        Assert.Throws<PluginInstallException>(() => PluginInstaller.Install(Root, machine, ExecutionScope.User, null));
        PluginInstaller.Install(Root, machine, ExecutionScope.Machine, null);

        Assert.Throws<PluginInstallException>(() => PluginInstaller.Rollback(Root, "acme.usb"));
    }

    [Fact]
    public void Plugins_marked_for_removal_are_not_loaded_and_are_removed_later()
    {
        PluginInstaller.Install(Root, Package("1.0.0"), ExecutionScope.User, null);
        var state = InstalledPluginsFile.Read(Root);
        state.Find("acme.usb")!.Remove = true;
        state.Write(Root);

        Assert.Empty(PluginStore.Discover(Root, ExecutionScope.User));

        PluginInstaller.FinishRemovals(Root);
        Assert.False(Directory.Exists(Path.Combine(Root, "acme.usb")));
        Assert.Null(InstalledPluginsFile.Read(Root).Find("acme.usb"));
    }

    private const string Releases = """
        [
          {"tag_name":"v2.0.0-beta.1","prerelease":true,"draft":false,"html_url":"https://github.com/acme/usb/releases/tag/v2.0.0-beta.1",
           "assets":[{"name":"acme.usb-2.0.0-beta.1.aspkg","browser_download_url":"https://github.com/acme/usb/releases/download/v2.0.0-beta.1/acme.usb-2.0.0-beta.1.aspkg"}]},
          {"tag_name":"v1.3.0","prerelease":false,"draft":true,"assets":[{"name":"x.aspkg","browser_download_url":"https://github.com/x"}]},
          {"tag_name":"v1.2.0","prerelease":false,"draft":false,"html_url":"https://github.com/acme/usb/releases/tag/v1.2.0",
           "assets":[
             {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/acme/usb/releases/download/v1.2.0/SHA256SUMS.txt"},
             {"name":"acme.usb-1.2.0.aspkg","browser_download_url":"https://github.com/acme/usb/releases/download/v1.2.0/acme.usb-1.2.0.aspkg","digest":"sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789"}]},
          {"tag_name":"v1.1.0","prerelease":false,"draft":false,"assets":[{"name":"notes.txt","browser_download_url":"https://github.com/n"}]}
        ]
        """;

    [Fact]
    public void Releases_pick_the_newest_package()
    {
        var stable = PluginReleases.FindLatest(Releases)!;
        Assert.Equal("1.2.0", stable.Version.ToString());
        Assert.Equal("acme.usb-1.2.0.aspkg", stable.AssetName);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", stable.ApiSha256);
        Assert.NotNull(stable.ChecksumsUrl);

        Assert.Equal("2.0.0-beta.1", PluginReleases.FindLatest(Releases, includePrereleases: true)!.Version.ToString());
        Assert.Null(PluginReleases.FindLatest(Releases, newerThan: SemVersion.Parse("1.2.0")));
        Assert.Null(PluginReleases.FindLatest(Releases, "*.zip"));
    }

    [Theory]
    [InlineData("acme/usb", "acme/usb")]
    [InlineData("github:acme/usb", "acme/usb")]
    [InlineData("https://github.com/acme/usb", "acme/usb")]
    [InlineData("https://github.com/acme/usb.git", "acme/usb")]
    [InlineData("https://github.com/acme/usb/releases/tag/v1.0.0", "acme/usb")]
    [InlineData("https://evil.com/acme/usb", null)]
    [InlineData("C:\\plugins\\x.aspkg", null)]
    [InlineData("acme", null)]
    public void Repositories_are_recognised(string text, string? expected)
    {
        Assert.Equal(expected is not null, PluginReleases.TryParseRepository(text, out var repository));
        if (expected is not null)
            Assert.Equal(expected, repository);
    }
}
