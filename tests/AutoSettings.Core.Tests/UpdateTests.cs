using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Tests;

public class UpdateTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("0.2.0", "0.3.0")]
    [InlineData("v0.2.0", "0.2.1")]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("0.3.0-beta.1", "0.3.0")]
    [InlineData("0.3.0-alpha", "0.3.0-beta")]
    [InlineData("0.3.0-beta.2", "0.3.0-beta.10")]
    [InlineData("0.3.0-beta", "0.3.0-beta.1")]
    [InlineData("0.3.0-1", "0.3.0-beta")]
    [InlineData("1.2", "1.2.1")]
    public void Versions_are_ordered(string lower, string higher)
    {
        var a = SemVersion.Parse(lower);
        var b = SemVersion.Parse(higher);
        Assert.True(a < b);
        Assert.True(b > a);
    }

    [Theory]
    [InlineData("0.3.0+abc", "0.3.0")]
    [InlineData("v1.2", "1.2.0")]
    public void Equal_versions(string a, string b) => Assert.Equal(SemVersion.Parse(a), SemVersion.Parse(b));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("-1.2.3")]
    public void Invalid_versions(string text) => Assert.Null(SemVersion.TryParse(text));

    [Fact]
    public void Msi_version_drops_the_prerelease_label()
    {
        Assert.Equal("0.3.0", SemVersion.Parse("v0.3.0-beta.1").ToMsiVersion());
        Assert.Equal("0.3.0-beta.1", SemVersion.Parse("v0.3.0-beta.1+sha").ToString());
    }

    private static string Release(string tag, bool prerelease = false, bool draft = false, bool msi = true, bool sums = true, string? digest = null)
    {
        var version = tag.TrimStart('v');
        var assets = new List<string>();
        if (msi)
        {
            var digestProperty = digest is null ? "" : ", \"digest\": \"" + digest + "\"";
            assets.Add("{ \"name\": \"AutoSettings-" + version + "-x64.msi\", \"browser_download_url\": \"https://example/" + tag + ".msi\"" + digestProperty + " }");
        }
        if (sums)
            assets.Add("{ \"name\": \"SHA256SUMS.txt\", \"browser_download_url\": \"https://example/" + tag + "/SHA256SUMS.txt\" }");
        assets.Add("{ \"name\": \"AutoSettings-" + version + "-win-x64.zip\", \"browser_download_url\": \"https://example/" + tag + ".zip\" }");

        return "{ \"tag_name\": \"" + tag + "\", "
            + "\"draft\": " + (draft ? "true" : "false") + ", "
            + "\"prerelease\": " + (prerelease ? "true" : "false") + ", "
            + "\"html_url\": \"https://github.com/o/r/releases/tag/" + tag + "\", "
            + "\"body\": \"Notes for " + tag + "\", "
            + "\"assets\": [" + string.Join(", ", assets) + "] }";
    }

    private static string Feed(params string[] releases) => "[" + string.Join(",", releases) + "]";

    [Fact]
    public void Picks_the_newest_stable_release()
    {
        var json = Feed(Release("v0.4.0-beta.1", prerelease: true), Release("v0.3.1"), Release("v0.3.0"), Release("v0.2.0"));
        var update = ReleaseFeed.FindUpdate(json, SemVersion.Parse("0.2.0"), includePrereleases: false);

        Assert.NotNull(update);
        Assert.Equal("0.3.1", update.Version);
        Assert.Equal("v0.3.1", update.Tag);
        Assert.Equal("AutoSettings-0.3.1-x64.msi", update.MsiName);
        Assert.Equal("https://example/v0.3.1.msi", update.MsiUrl);
        Assert.Equal("https://example/v0.3.1/SHA256SUMS.txt", update.ChecksumsUrl);
        Assert.Equal("Notes for v0.3.1", update.Notes);
        Assert.False(update.IsPrerelease);
    }

    [Fact]
    public void Offers_betas_only_when_opted_in()
    {
        var json = Feed(Release("v0.4.0-beta.1", prerelease: true), Release("v0.3.0"));
        Assert.Equal("0.4.0-beta.1", ReleaseFeed.FindUpdate(json, SemVersion.Parse("0.3.0"), includePrereleases: true)?.Version);
        Assert.Null(ReleaseFeed.FindUpdate(json, SemVersion.Parse("0.3.0"), includePrereleases: false));
    }

    [Fact]
    public void A_version_tag_with_a_label_counts_as_beta_even_if_not_marked()
    {
        var json = Feed(Release("v0.4.0-rc.1"));
        Assert.Null(ReleaseFeed.FindUpdate(json, SemVersion.Parse("0.3.0"), includePrereleases: false));
    }

    [Fact]
    public void The_final_release_replaces_its_beta()
    {
        var json = Feed(Release("v0.4.0"), Release("v0.4.0-beta.1", prerelease: true));
        Assert.Equal("0.4.0", ReleaseFeed.FindUpdate(json, SemVersion.Parse("0.4.0-beta.1"), includePrereleases: true)?.Version);
    }

    [Fact]
    public void Skips_drafts_releases_without_an_installer_and_older_versions()
    {
        var json = Feed(Release("v0.5.0", draft: true), Release("v0.4.0", msi: false), Release("v0.1.0"), Release("nightly"));
        Assert.Null(ReleaseFeed.FindUpdate(json, SemVersion.Parse("0.2.0"), includePrereleases: true));
    }

    [Fact]
    public void Up_to_date_returns_nothing()
    {
        var json = Feed(Release("v0.2.0"));
        Assert.Null(ReleaseFeed.FindUpdate(json, SemVersion.Parse("0.2.0"), includePrereleases: false));
        Assert.Null(ReleaseFeed.FindUpdate("[]", SemVersion.Parse("0.2.0"), includePrereleases: false));
        Assert.Null(ReleaseFeed.FindUpdate("{\"message\":\"rate limited\"}", SemVersion.Parse("0.2.0"), includePrereleases: false));
    }

    [Fact]
    public void Reads_the_asset_digest_and_missing_checksums()
    {
        var json = Feed(Release("v0.3.0", sums: false, digest: "sha256:" + Sha.ToUpperInvariant()));
        var update = ReleaseFeed.FindUpdate(json, SemVersion.Parse("0.2.0"), includePrereleases: false);
        Assert.NotNull(update);
        Assert.Equal(Sha, update.ApiSha256);
        Assert.Null(update.ChecksumsUrl);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("md5:abc", null)]
    [InlineData("sha256:xyz", null)]
    [InlineData("sha256:" + Sha, Sha)]
    public void Digests(string? digest, string? expected) => Assert.Equal(expected, ReleaseFeed.ParseDigest(digest));

    [Theory]
    [InlineData("AutoSettings-0.3.0-x64.msi", true)]
    [InlineData("autosettings-0.3.0-beta.1-x64.msi", true)]
    [InlineData("AutoSettings-0.3.0-win-x64.zip", false)]
    [InlineData("Other-0.3.0-x64.msi", false)]
    public void Installer_names(string name, bool expected) => Assert.Equal(expected, ReleaseFeed.IsInstaller(name));

    [Fact]
    public void Parses_checksum_files()
    {
        var text = $"""
            # comment
            {Sha.ToUpperInvariant()}  AutoSettings-0.3.0-x64.msi
            {Sha} *AutoSettings-0.3.0-win-x64.zip
            not-a-hash  something.txt

            """;
        var sums = Checksums.Parse(text.Replace("\n", "\r\n"));
        Assert.Equal(2, sums.Count);
        Assert.Equal(Sha, sums["autosettings-0.3.0-x64.msi"]);
        Assert.Equal(Sha, sums["AutoSettings-0.3.0-win-x64.zip"]);
        Assert.Equal($"{Sha}  a.msi", Checksums.FormatLine(Sha.ToUpperInvariant(), "a.msi"));
    }

    private static readonly ReleaseInfo SomeRelease = new("0.3.0", "v0.3.0", false, "", "", "https://x/a.msi", "a.msi", null, null);

    [Theory]
    [InlineData(UpdateMode.Off, InstallMethod.Msi, false, UpdateStep.None)]
    [InlineData(UpdateMode.Notify, InstallMethod.Msi, false, UpdateStep.Notify)]
    [InlineData(UpdateMode.AskFirst, InstallMethod.Msi, false, UpdateStep.Download)]
    [InlineData(UpdateMode.AskFirst, InstallMethod.Msi, true, UpdateStep.Notify)]
    [InlineData(UpdateMode.Automatic, InstallMethod.Msi, false, UpdateStep.Download)]
    [InlineData(UpdateMode.Automatic, InstallMethod.Msi, true, UpdateStep.Install)]
    [InlineData(UpdateMode.Off, InstallMethod.Portable, false, UpdateStep.None)]
    [InlineData(UpdateMode.Notify, InstallMethod.Portable, false, UpdateStep.Notify)]
    [InlineData(UpdateMode.AskFirst, InstallMethod.Portable, false, UpdateStep.Notify)]
    [InlineData(UpdateMode.Automatic, InstallMethod.Portable, true, UpdateStep.Notify)]
    public void Planner_steps(UpdateMode mode, InstallMethod method, bool downloaded, UpdateStep expected) =>
        Assert.Equal(expected, UpdatePlanner.NextStep(mode, method, SomeRelease, downloaded));

    [Fact]
    public void Planner_does_nothing_when_up_to_date()
    {
        foreach (var mode in Enum.GetValues<UpdateMode>())
            Assert.Equal(UpdateStep.None, UpdatePlanner.NextStep(mode, InstallMethod.Msi, null, false));
    }

    [Fact]
    public void Only_msi_installs_can_install()
    {
        Assert.True(UpdatePlanner.CanInstall(InstallMethod.Msi, SomeRelease, UpdateState.Ready));
        Assert.True(UpdatePlanner.CanInstall(InstallMethod.Msi, SomeRelease, UpdateState.Available));
        Assert.False(UpdatePlanner.CanInstall(InstallMethod.Portable, SomeRelease, UpdateState.Ready));
        Assert.False(UpdatePlanner.CanInstall(InstallMethod.Msi, null, UpdateState.UpToDate));
        Assert.False(UpdatePlanner.CanInstall(InstallMethod.Msi, SomeRelease, UpdateState.Installing));
        Assert.False(UpdatePlanner.CanInstall(InstallMethod.Msi, SomeRelease, UpdateState.Downloading));
    }

    [Fact]
    public void Automatic_install_waits_for_full_screen_apps_but_not_forever()
    {
        var downloaded = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var day = TimeSpan.FromHours(24);
        Assert.True(UpdatePlanner.MayInstallAutomatically(false, downloaded, downloaded, day));
        Assert.False(UpdatePlanner.MayInstallAutomatically(true, downloaded, downloaded.AddHours(3), day));
        Assert.True(UpdatePlanner.MayInstallAutomatically(true, downloaded, downloaded.AddHours(24), day));
    }
}
