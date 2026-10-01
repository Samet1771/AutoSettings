using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Plugins;
using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Tests;

public class PluginManifestTests
{
    // Windows checkouts may use CRLF; the tests below edit the text with "\n".
    internal static readonly string UsbScript = UsbScriptText.ReplaceLineEndings("\n");

    private const string UsbScriptText = """
        id: acme.usb
        name:
          en: USB tools
          tr: USB araçları
        description: Reacts to USB drives.
        version: 1.2.0
        publisher: Acme
        kind: script
        scope: user
        sdk: "1.0"
        min_app_version: 0.3.0
        permissions: [powershell]
        update:
          github: acme/autosettings-usb
        components:
          - type: acme.usb.connected
            kind: trigger
            title:
              en: USB drive connected
              tr: USB sürücü takıldı
            description: Fires when a USB drive is connected.
            opposite: acme.usb.disconnected
            interval: 10s
            scripts:
              poll: poll.ps1
            fields:
              - name: drive
                type: string
                description: Drive letter, for example E:.
          - type: acme.usb.disconnected
            kind: trigger
            title: USB drive removed
            description: Fires when a USB drive is removed.
          - type: acme.usb.label
            kind: action
            title: Rename a drive
            description: Sets the volume label of a drive.
            revertible: true
            scripts:
              apply: label/set.ps1
              capture: label/get.ps1
              restore: label/set.ps1
            fields:
              - name: drive
                type: string
                description: Drive letter.
                required: true
                key: true
                example: "E:"
              - name: label
                type: string
                description: New label.
                required: true
          - type: acme.usb.present
            kind: condition
            title: USB drive present
            description: True while a drive is connected.
            scripts:
              evaluate: present.ps1
            fields:
              - name: mode
                type: enum
                description: Which drives count.
                values: [any, removable]
                default: any
        """;

    private static List<ConfigIssue> Errors(string yaml, SemVersion? app = null) =>
        PluginManifestReader.Load(yaml, app).Issues.Where(i => i.Severity == IssueSeverity.Error).ToList();

    [Fact]
    public void A_valid_script_manifest_loads()
    {
        var (manifest, issues) = PluginManifestReader.Load(UsbScript, SemVersion.Parse("0.3.0"));

        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
        Assert.Equal("acme.usb", manifest.Id);
        Assert.Equal("USB araçları", manifest.Name.Translations["tr"]);
        Assert.Equal(PluginKind.Script, manifest.Kind);
        Assert.Equal(4, manifest.Components.Count);
        Assert.Equal("label/get.ps1", manifest.Components[2].Scripts.Capture);
        Assert.Equal(TimeSpan.FromSeconds(10), manifest.Components[0].Interval);
        Assert.Equal("acme/autosettings-usb", manifest.Update!.GitHub);
        Assert.Equal("*.aspkg", manifest.Update.Asset);
    }

    [Fact]
    public void Manifest_components_become_catalog_entries()
    {
        var (manifest, _) = PluginManifestReader.Load(UsbScript);
        var contribution = ManifestMapping.ToContribution(manifest);
        var catalog = ComponentCatalog.Compose(BuiltInComponents.All, [contribution]);

        Assert.Empty(catalog.Rejected);
        var connected = catalog.Catalog.Find(ComponentKind.Trigger, "acme.usb.connected")!;
        Assert.Equal(SystemEventKind.Plugin, connected.EventKind);
        Assert.Equal("acme.usb.disconnected", connected.OppositeEvent);
        Assert.Equal("USB sürücü takıldı", connected.Localized["tr"].Title);
        Assert.Equal("USB tools", connected.Category);
        Assert.Equal("user", connected.Fields[0].Name);
        Assert.Equal("acme.usb", connected.Source.PluginId);

        var label = catalog.Catalog.Find(ComponentKind.Action, "acme.usb.label")!;
        Assert.True(label.Revertible);
        Assert.Equal(new[] { "drive" }, label.KeyFields);
        Assert.Contains("drive: E:", label.Example);

        // The catalog entries work in an automation.
        var result = ConfigLoader.Load("""
            version: 1
            automations:
              - id: a
                triggers:
                  - type: acme.usb.connected
                    drive: "E:"
                conditions:
                  - type: acme.usb.present
                actions:
                  - type: acme.usb.label
                    drive: "E:"
                    label: Backup
            """, ExecutionScope.User, catalog.Catalog);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        Assert.Equal("any", catalog.Catalog.WithDefaults(ComponentKind.Condition, result.Config.Automations[0].Conditions[0]).GetString("mode"));
    }

    [Theory]
    [InlineData("id: acme.usb", "id: Acme USB", "not a valid plugin id")]
    [InlineData("version: 1.2.0", "version: one", "version")]
    [InlineData("sdk: \"1.0\"", "sdk: \"2.0\"", "supports SDK 1.x")]
    [InlineData("kind: script", "kind: python", "must be one of")]
    [InlineData("type: acme.usb.label", "type: other.usb.label", "the type must be the plugin id")]
    [InlineData("type: acme.usb.label", "type: acme.usb.Label", "the type must be the plugin id")]
    [InlineData("apply: label/set.ps1", "apply: ../../evil.ps1", "inside the plugin folder")]
    [InlineData("apply: label/set.ps1", "apply: C:\\\\evil.ps1", "inside the plugin folder")]
    [InlineData("capture: label/get.ps1", "", "need 'scripts.capture' and 'scripts.restore'")]
    [InlineData("values: [any, removable]", "", "enum fields need 'values'")]
    [InlineData("type: enum", "type: colour", "unknown field type")]
    [InlineData("name: label", "name: user", "reserved")]
    [InlineData("permissions: [powershell]", "permissions: [telepathy]", "unknown permission")]
    [InlineData("github: acme/autosettings-usb", "github: not a repo", "not a GitHub repository")]
    [InlineData("revertible: true", "revertible: true\n    colour: red", "unknown key 'colour'")]
    public void Invalid_manifests_are_rejected(string find, string replace, string expected)
    {
        Assert.Contains(find, UsbScript);
        var errors = Errors(UsbScript.Replace(find, replace));

        Assert.Contains(errors, e => e.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Too_old_app_version_is_rejected()
    {
        Assert.Contains(Errors(UsbScript, SemVersion.Parse("0.2.0")), e => e.Message.Contains("0.3.0 or newer"));
        Assert.Contains(Errors(UsbScript, SemVersion.Parse("0.2.9-beta.1")), e => e.Message.Contains("0.3.0 or newer"));
        // Betas of the required version are new enough.
        Assert.DoesNotContain(Errors(UsbScript, SemVersion.Parse("0.3.0-beta.2")), e => e.Message.Contains("or newer"));
        Assert.DoesNotContain(Errors(UsbScript, SemVersion.Parse("0.4.0")), e => e.Message.Contains("or newer"));
    }

    [Fact]
    public void Machine_actions_need_a_machine_plugin_and_the_system_permission()
    {
        var yaml = UsbScript.Replace("revertible: true", "revertible: true\n    runs_as: machine");

        Assert.Contains(Errors(yaml), e => e.Message.Contains("scope: machine"));
        Assert.Contains(Errors(yaml.Replace("scope: user", "scope: machine")), e => e.Message.Contains("run_as_system"));
        Assert.Empty(Errors(yaml.Replace("scope: user", "scope: machine").Replace("[powershell]", "[powershell, run_as_system]")));
    }

    [Fact]
    public void Dotnet_plugins_need_a_dll_entry()
    {
        var dotnet = ("""
            id: acme.hello
            name: Hello
            version: 1.0.0
            publisher: Acme
            kind: dotnet
            sdk: "1.0"
            entry: Acme.Hello.dll
            components:
              - type: acme.hello.say
                kind: action
                title: Say hello
                description: Shows a message.
            """).ReplaceLineEndings("\n");

        Assert.Empty(Errors(dotnet));
        Assert.Contains(Errors(dotnet.Replace("entry: Acme.Hello.dll", "entry: ..\\\\Acme.Hello.dll")), e => e.Message.Contains("inside the plugin folder"));
        Assert.Contains(Errors(dotnet.Replace("entry: Acme.Hello.dll\n", "")), e => e.Message.Contains("entry: required"));
        Assert.Contains(Errors(dotnet.Replace("kind: action\n", "kind: action\n    scripts:\n      apply: a.ps1\n")), e => e.Message.Contains("only for script plugins"));
    }

    [Fact]
    public void Script_plugins_always_declare_powershell()
    {
        var (manifest, issues) = PluginManifestReader.Load(UsbScript.Replace("permissions: [powershell]", "permissions: []"));

        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
        Assert.Contains(PluginPermission.PowerShell, manifest.Permissions);
    }

    [Fact]
    public void Syntax_errors_are_reported_not_thrown()
    {
        var (_, issues) = PluginManifestReader.Load("id: [unclosed");

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.StartsWith("YAML", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("scripts/run.ps1", true)]
    [InlineData("run.ps1", true)]
    [InlineData("../run.ps1", false)]
    [InlineData("a/../../run.ps1", false)]
    [InlineData("C:\\run.ps1", false)]
    [InlineData("\\\\server\\share\\run.ps1", false)]
    [InlineData("/etc/run.ps1", false)]
    [InlineData("", false)]
    public void Script_paths_must_stay_in_the_plugin_folder(string path, bool safe)
    {
        Assert.Equal(safe, PluginManifestValidator.IsSafeRelativePath(path));
    }

    [Fact]
    public void The_example_in_the_manifest_docs_is_valid()
    {
        var root = TestSupport.FindRepositoryRoot();
        if (root is null)
            return;
        var text = File.ReadAllText(Path.Combine(root, "docs", "plugins", "manifest.md")).ReplaceLineEndings("\n");
        var start = text.IndexOf("```yaml\n", StringComparison.Ordinal) + "```yaml\n".Length;
        var yaml = text[start..text.IndexOf("```", start, StringComparison.Ordinal)];

        var (manifest, issues) = PluginManifestReader.Load(yaml);

        Assert.True(issues.Count == 0, string.Join("\n", issues));
        Assert.Equal("acme.usb", manifest.Id);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("dotnet")]
    public void The_dotnet_new_template_manifests_are_valid(string kind)
    {
        var root = TestSupport.FindRepositoryRoot();
        if (root is null)
            return;
        // What "dotnet new autosettings-plugin --publisher acme -n UsbTools" produces.
        var yaml = File.ReadAllText(Path.Combine(root, "templates", "content", "autosettings-plugin", kind, "plugin.yaml"))
            .Replace("mypublisher", "acme").Replace("myplugin", "usbtools").Replace("MyPlugin", "UsbTools");
        var (manifest, issues) = PluginManifestReader.Read(yaml);
        Assert.Empty(issues);
        if (kind == "dotnet")
        {
            // pack adds the components from the attributes.
            manifest.Components = SdkDescriber.Describe([typeof(TemplateLikeAction)]);
        }
        Assert.Empty(PluginManifestValidator.Validate(manifest).Where(i => i.Severity == IssueSeverity.Error));
        Assert.Equal("acme.usbtools", manifest.Id);
    }

    [AutoSettings.Sdk.PluginComponent("acme.usbtools.say_hello", Title = "Say hello", Description = "Test.")]
    [AutoSettings.Sdk.Field("name", AutoSettings.Sdk.FieldKind.String, Description = "Who.", Required = true)]
    private sealed class TemplateLikeAction : AutoSettings.Sdk.IPluginAction
    {
        public Task ExecuteAsync(AutoSettings.Sdk.ActionRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void Permissions_have_names_and_descriptions()
    {
        foreach (var permission in Enum.GetValues<PluginPermission>())
        {
            Assert.Equal(permission, PluginPermissions.Parse(PluginPermissions.Name(permission)));
            Assert.False(string.IsNullOrWhiteSpace(PluginPermissions.Describe(permission)));
        }
        Assert.Null(PluginPermissions.Parse("nope"));
    }
}
