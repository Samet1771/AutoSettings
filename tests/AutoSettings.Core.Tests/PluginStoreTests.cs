using System.Text.Json;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Plugins;

namespace AutoSettings.Core.Tests;

public sealed class PluginStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("autosettings-store-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Install(string id, string version, string? yaml = null, bool withScripts = true)
    {
        var directory = Path.Combine(_root, id, version);
        Directory.CreateDirectory(directory);
        yaml ??= PluginManifestTests.UsbScript.Replace("id: acme.usb", $"id: {id}").Replace("version: 1.2.0", $"version: {version}").Replace("acme.usb.", id + ".");
        File.WriteAllText(Path.Combine(directory, PluginStore.ManifestFileName), yaml);
        if (withScripts)
        {
            foreach (var script in new[] { "poll.ps1", "present.ps1", "label/set.ps1", "label/get.ps1" })
            {
                var path = Path.Combine(directory, script);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "# test");
            }
        }
        return directory;
    }

    [Fact]
    public void The_highest_version_is_used_when_nothing_was_installed_properly()
    {
        Install("acme.usb", "1.2.0");
        Install("acme.usb", "1.10.0");

        var plugin = Assert.Single(PluginStore.Discover(_root, ExecutionScope.User));

        Assert.True(plugin.IsActive, string.Join("\n", plugin.Issues));
        Assert.Equal("1.10.0", plugin.Manifest!.Version);
    }

    [Fact]
    public void Installed_json_chooses_the_version_and_can_turn_plugins_off()
    {
        Install("acme.usb", "1.2.0");
        Install("acme.usb", "1.10.0");
        new InstalledPluginsFile { Plugins = [new InstalledPluginEntry { Id = "acme.usb", Version = "1.2.0", Enabled = false, Sha256 = "abc" }] }.Write(_root);

        var plugin = Assert.Single(PluginStore.Discover(_root, ExecutionScope.User));

        Assert.Equal("1.2.0", plugin.Manifest!.Version);
        Assert.True(plugin.IsValid);
        Assert.False(plugin.IsActive);
        Assert.Equal("abc", plugin.PackageSha256);
        Assert.DoesNotContain(PluginStore.Compose([plugin]).Catalog.All, d => !d.Source.IsBuiltIn);
    }

    [Fact]
    public void Folder_names_must_match_the_manifest_and_scripts_must_exist()
    {
        var directory = Install("acme.usb", "1.2.0", withScripts: false);
        Directory.Move(directory, Path.Combine(_root, "acme.usb", "9.9.9"));

        var plugin = Assert.Single(PluginStore.Discover(_root, ExecutionScope.User));

        Assert.False(plugin.IsValid);
        Assert.Contains(plugin.Issues, i => i.Message.Contains("version is '1.2.0'"));
        Assert.Contains(plugin.Issues, i => i.Message.Contains("poll.ps1 is missing"));
    }

    [Fact]
    public void Machine_plugins_cannot_be_installed_per_user()
    {
        Install("acme.usb", "1.2.0", PluginManifestTests.UsbScript.Replace("scope: user", "scope: machine"));

        Assert.Contains(Assert.Single(PluginStore.Discover(_root, ExecutionScope.User)).Issues, i => i.Message.Contains("administrator"));
        Assert.True(Assert.Single(PluginStore.Discover(_root, ExecutionScope.Machine)).IsValid);
    }

    [Fact]
    public void Staging_folders_and_empty_roots_are_ignored()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".staging", "x"));
        Directory.CreateDirectory(Path.Combine(_root, "_backup"));

        Assert.Empty(PluginStore.Discover(_root, ExecutionScope.User));
        Assert.Empty(PluginStore.Discover(Path.Combine(_root, "missing"), ExecutionScope.User));
    }

    [Fact]
    public void Machine_plugins_win_over_user_plugins_with_the_same_types()
    {
        var machine = PluginStore.Load(Install("acme.usb", "1.2.0"), "acme.usb", ExecutionScope.Machine);
        var user = PluginStore.Load(Install("acme.usb", "1.2.0"), "acme.usb", ExecutionScope.User);

        var composition = PluginStore.Compose([user, machine]);

        Assert.Equal(ExecutionScope.Machine, composition.Catalog.Find(ComponentKind.Action, "acme.usb.label")!.Source.PluginScope);
        Assert.Single(composition.Rejected);
    }

    [Fact]
    public void Requests_carry_parameters_event_and_snapshot()
    {
        var plugin = PluginStore.Load(Install("acme.usb", "1.2.0"), "acme.usb", ExecutionScope.User);
        var e = new SystemEvent
        {
            Kind = SystemEventKind.Plugin,
            PluginEvent = "acme.usb.connected",
            SessionId = 2,
            User = TestSupport.Samet,
            Data = new Dictionary<string, string> { ["drive"] = "E:" },
        };
        var parameters = new Dictionary<string, object?> { ["drive"] = "E:", ["wait"] = TimeSpan.FromSeconds(30), ["labels"] = new List<string> { "a", "b" } };

        using var json = JsonDocument.Parse(PluginRequest.Build(plugin, "acme.usb.label", PluginOperation.Restore, parameters, e, TestSupport.Samet, "Backup", "old", "C:\\state"));
        var root = json.RootElement;

        Assert.Equal("restore", root.GetProperty("operation").GetString());
        Assert.Equal("acme.usb", root.GetProperty("plugin").GetProperty("id").GetString());
        Assert.Equal("30s", root.GetProperty("parameters").GetProperty("wait").GetString());
        Assert.Equal(2, root.GetProperty("parameters").GetProperty("labels").GetArrayLength());
        Assert.Equal("old", root.GetProperty("snapshot").GetString());
        Assert.Equal("acme.usb.connected", root.GetProperty("event").GetProperty("name").GetString());
        Assert.Equal("E:", root.GetProperty("event").GetProperty("data").GetProperty("drive").GetString());
        Assert.Equal("samet", root.GetProperty("user").GetProperty("name").GetString());

        var environment = PluginRequest.Environment(plugin, PluginOperation.Restore, parameters, "old", "C:\\state");
        Assert.Equal("restore", environment["AUTOSETTINGS_OPERATION"]);
        Assert.Equal("30s", environment["AUTOSETTINGS_PARAM_WAIT"]);
        Assert.Equal("a, b", environment["AUTOSETTINGS_PARAM_LABELS"]);
        Assert.Equal("old", environment["AUTOSETTINGS_SNAPSHOT"]);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False\n", false)]
    [InlineData("checking...\r\nTRUE\r\n", true)]
    [InlineData("yes", null)]
    [InlineData("", null)]
    public void Condition_output_is_the_last_line(string output, bool? expected)
    {
        Assert.Equal(expected, PluginOutput.ParseCondition(output));
    }

    [Fact]
    public void Poll_output_becomes_events()
    {
        var output = """
            acme.usb.connected
            {"event":"acme.usb.disconnected","data":{"drive":"E:","size":12},"user":"samet"}
            other.plugin.event
            {"event":
            just some text
            """;

        var events = PluginOutput.ParseEvents("acme.usb", output, out var ignored);

        Assert.Equal(2, events.Count);
        Assert.Equal("acme.usb.connected", events[0].Name);
        Assert.Equal("E:", events[1].Data["drive"]);
        Assert.Equal("12", events[1].Data["size"]);
        Assert.Equal("samet", events[1].User);
        Assert.Equal(3, ignored.Count);
    }
}
