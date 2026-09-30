using AutoSettings.Sdk;

namespace AutoSettings.Core.Tests;

public class SdkTests
{
    [Fact]
    public void Parameters_read_typed_values()
    {
        var p = Parameters.FromJson("""{"drive":"E:","count":3,"ratio":0.5,"force":true,"labels":["a","b"],"wait":"1h30m","at":"22:00","off":null}""");

        Assert.Equal("E:", p.GetString("DRIVE"));
        Assert.Equal(3, p.GetInteger("count"));
        Assert.Equal(0.5, p.GetNumber("ratio"));
        Assert.True(p.GetBoolean("force"));
        Assert.Equal("true", p.GetString("force"));
        Assert.Equal(new[] { "a", "b" }, p.GetStringList("labels"));
        Assert.Equal(new[] { "E:" }, p.GetStringList("drive"));
        Assert.Empty(p.GetStringList("missing"));
        Assert.Equal(TimeSpan.FromMinutes(90), p.GetDuration("wait"));
        Assert.Equal(new TimeOnly(22, 0), p.GetTime("at"));
        Assert.False(p.Has("off"));
        Assert.Null(p.GetString("missing"));
        Assert.Null(p.GetInteger("drive"));
    }

    [Theory]
    [InlineData("30s", 30_000)]
    [InlineData("500ms", 500)]
    [InlineData("1h 30m", 5_400_000)]
    [InlineData("2d", 172_800_000)]
    [InlineData("01:00:00", 3_600_000)]
    public void Durations_parse(string text, long milliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), Parameters.ParseDuration(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("30x")]
    public void Bad_durations_are_null(string text)
    {
        Assert.Null(Parameters.ParseDuration(text));
    }

    [Fact]
    public void Parameters_can_be_built_for_tests()
    {
        var p = Parameters.From(new Dictionary<string, object?> { ["drive"] = "E:", ["labels"] = new[] { "x" }, ["n"] = 2 });

        Assert.Equal("E:", p.GetString("drive"));
        Assert.Equal(new[] { "x" }, p.GetStringList("labels"));
        Assert.Equal(2, p.GetInteger("n"));
        Assert.Equal(3, p.Names.Count());
    }

    [Fact]
    public void Sdk_version_constants_agree()
    {
        Assert.StartsWith(SdkInfo.MajorVersion + ".", SdkInfo.Version, StringComparison.Ordinal);
        Assert.Contains(SdkInfo.MajorVersion, AutoSettings.Core.Plugins.PluginManifestValidator.SupportedSdkMajorVersions);
    }
}
