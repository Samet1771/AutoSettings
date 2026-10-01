using System.IO.Compression;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Plugins;
using AutoSettings.Sdk;

namespace AutoSettings.Core.Tests;

[PluginComponent("acme.test.greet", Title = "Greet", Description = "Says hello.")]
[Localized("tr", "Selamla", Description = "Merhaba der.")]
[Field("name", FieldKind.String, Description = "Who to greet.", Required = true, KeyField = true, Example = "samet")]
[Field("times", FieldKind.Integer, Description = "How often.", Default = "1", Minimum = 1, Maximum = 5)]
[Field("tone", FieldKind.Enum, Description = "Tone.", Values = new[] { "warm", "short" }, Default = "warm")]
public sealed class GreetAction : IRevertiblePluginAction
{
    public Task ExecuteAsync(ActionRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string?> CaptureAsync(ActionRequest request, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task RestoreAsync(ActionRequest request, string? snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
}

[PluginComponent("acme.test.on", Title = "On", Description = "Turned on.", OppositeEvent = "acme.test.off")]
[PluginComponent("acme.test.off", Title = "Off", Description = "Turned off.", AvailableIn = PluginAvailability.User)]
[Localized("tr", "Açıldı", Type = "acme.test.on")]
[Field("device", FieldKind.String, Description = "Which device.")]
public sealed class SwitchTrigger : IPluginTrigger
{
    public Task RunAsync(ITriggerSink sink, CancellationToken cancellationToken) => Task.CompletedTask;
}

[PluginComponent("acme.test.bad", Title = "Bad", Description = "Not a component.")]
public sealed class NotAComponent
{
}

public sealed class PluginPackageTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("autosettings-package-").FullName;

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

    private static PluginManifest DotnetManifest() => new()
    {
        Id = "acme.test",
        Name = new LocalizedString("Test", new Dictionary<string, string> { ["tr"] = "Deneme" }),
        Version = "1.0.0",
        Publisher = "Acme",
        Kind = PluginKind.Dotnet,
        Sdk = "1.0",
        Entry = "Acme.Test.dll",
        Components = SdkDescriber.Describe([typeof(GreetAction), typeof(SwitchTrigger)]),
    };

    [Fact]
    public void Attributes_become_manifest_components()
    {
        var components = DotnetManifest().Components;

        Assert.Equal(new[] { "acme.test.greet", "acme.test.on", "acme.test.off" }, components.Select(c => c.Type));
        var greet = components[0];
        Assert.Equal(ComponentKind.Action, greet.Kind);
        Assert.True(greet.Revertible);
        Assert.Equal("Selamla", greet.Title.Translations["tr"]);
        Assert.Equal("Merhaba der.", greet.Description.Translations["tr"]);
        Assert.True(greet.Fields[0].Key);
        Assert.Equal(FieldType.Integer, greet.Fields[1].Type);
        Assert.Equal(5, greet.Fields[1].Maximum);
        Assert.Null(greet.Fields[0].Minimum);

        Assert.Equal("acme.test.off", components[1].Opposite);
        Assert.Equal("Açıldı", components[1].Title.Translations["tr"]);
        Assert.Empty(components[2].Title.Translations);
        Assert.Equal(ScopeSupport.User, components[2].AvailableIn);
        Assert.Equal("device", Assert.Single(components[2].Fields).Name);

        Assert.Throws<InvalidOperationException>(() => SdkDescriber.Describe(typeof(NotAComponent)));
    }

    [Fact]
    public void Every_sdk_field_kind_has_a_catalog_type_and_a_yaml_name()
    {
        foreach (var kind in Enum.GetValues<FieldKind>())
            Assert.Contains(SdkDescriber.ToFieldType(kind), PluginManifestReader.FieldTypes.Values);
    }

    [Fact]
    public void Written_manifests_read_back_the_same()
    {
        var manifest = DotnetManifest();
        manifest.Permissions = [PluginPermission.Network];
        manifest.Update = new PluginUpdateSource("acme/test");

        var yaml = PluginManifestWriter.Write(manifest);
        var (read, issues) = PluginManifestReader.Load(yaml);

        Assert.True(issues.Count == 0, yaml + "\n" + string.Join("\n", issues));
        Assert.Equal(PluginManifestWriter.Write(read), yaml);
        Assert.Equal("Deneme", read.Name.Translations["tr"]);
        Assert.Equal("1", read.Components[0].Fields[1].Default);
        Assert.Equal(new[] { "warm", "short" }, read.Components[0].Fields[2].Values);
        Assert.Contains("sdk: \"1.0\"", yaml);
    }

    private string PluginFolder(params string[] extraFiles)
    {
        var folder = Path.Combine(_temp, "build");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Acme.Test.dll"), "not really a dll");
        File.WriteAllText(Path.Combine(folder, "Acme.Test.pdb"), "symbols");
        foreach (var file in extraFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, file))!);
            File.WriteAllText(Path.Combine(folder, file), "x");
        }
        return folder;
    }

    [Fact]
    public void Packages_round_trip()
    {
        var manifest = DotnetManifest();
        var package = Path.Combine(_temp, "out", PluginPackage.FileName(manifest));

        var sha = PluginPackage.Create(PluginFolder("lib/helper.dll", "AutoSettings.Sdk.dll"), PluginManifestWriter.Write(manifest), package);

        Assert.Equal("acme.test-1.0.0.aspkg", Path.GetFileName(package));
        Assert.Equal(sha, PluginPackage.Sha256(package));
        var (inspected, issues) = PluginPackage.Inspect(package);
        Assert.True(issues.Count == 0, string.Join("\n", issues));
        Assert.Equal("acme.test", inspected!.Id);

        var target = Path.Combine(_temp, "installed");
        PluginPackage.Extract(package, target);
        Assert.True(File.Exists(Path.Combine(target, "plugin.yaml")));
        Assert.True(File.Exists(Path.Combine(target, "lib", "helper.dll")));
        Assert.False(File.Exists(Path.Combine(target, "Acme.Test.pdb")));
        Assert.False(File.Exists(Path.Combine(target, "AutoSettings.Sdk.dll")));
    }

    private string Zip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_temp, Guid.NewGuid().ToString("N") + ".aspkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("lib/../../evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("/etc/evil")]
    [InlineData("AutoSettings.Sdk.dll")]
    public void Malicious_packages_are_refused_before_extracting(string entry)
    {
        var package = Zip(("plugin.yaml", PluginManifestWriter.Write(DotnetManifest())), ("Acme.Test.dll", "x"), (entry, "boom"));

        var (_, issues) = PluginPackage.Inspect(package);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error);
        Assert.Throws<InvalidDataException>(() => PluginPackage.Extract(package, Path.Combine(_temp, "x")));
        Assert.False(File.Exists(Path.Combine(_temp, "evil.dll")));
    }

    [Fact]
    public void Packages_need_a_manifest_and_their_files()
    {
        Assert.Contains(PluginPackage.Inspect(Zip(("readme.txt", "hi"))).Issues, i => i.Message.Contains("no plugin.yaml"));
        Assert.Contains(PluginPackage.Inspect(Zip(("plugin.yaml", PluginManifestWriter.Write(DotnetManifest())))).Issues, i => i.Message.Contains("Acme.Test.dll is missing"));

        var notZip = Path.Combine(_temp, "broken.aspkg");
        File.WriteAllText(notZip, "this is not a zip");
        Assert.Contains(PluginPackage.Inspect(notZip).Issues, i => i.Message.Contains("Not a valid plugin package"));
    }
}
