using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;
using static AutoSettings.Core.Tests.TestSupport;

namespace AutoSettings.Core.Tests;

public class PluginTriggerTests
{
    private static readonly ComponentSource Usb = ComponentSource.Plugin("acme.usb", "1.0.0", ExecutionScope.User);

    private static readonly ComponentDescriptor Connected = new()
    {
        Type = "acme.usb.connected",
        Kind = ComponentKind.Trigger,
        Category = "USB",
        Title = "USB drive connected",
        Description = "Test trigger.",
        EventKind = SystemEventKind.Plugin,
        OppositeEvent = "acme.usb.disconnected",
        Fields =
        [
            Fields.Text("drive", "Drive letter."),
            Fields.List("labels", "Volume labels."),
            Fields.Boolean("removable", "Only removable drives."),
        ],
        Localized = new Dictionary<string, LocalizedText> { ["tr"] = new("USB sürücü takıldı") },
    };

    private static readonly ComponentDescriptor Disconnected = new()
    {
        Type = "acme.usb.disconnected",
        Kind = ComponentKind.Trigger,
        Category = "USB",
        Title = "USB drive removed",
        Description = "Test trigger.",
        EventKind = SystemEventKind.Plugin,
    };

    private static ComponentCatalog Catalog() =>
        ComponentCatalog.Compose(BuiltInComponents.All, [new PluginContribution(Usb, [Connected, Disconnected])]).Catalog;

    private static SystemEvent UsbEvent(string name, params (string Key, string Value)[] data) => new()
    {
        Kind = SystemEventKind.Plugin,
        PluginEvent = name,
        SessionId = 1,
        User = Samet,
        Data = data.ToDictionary(d => d.Key, d => d.Value),
    };

    private static bool Matches(string triggerYaml, SystemEvent e)
    {
        var catalog = Catalog();
        var result = ConfigLoader.Load($"""
            version: 1
            automations:
              - id: a
                triggers:
                  - {triggerYaml}
                actions:
                  - type: notify
                    message: hi
            """, ExecutionScope.User, catalog);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        var trigger = catalog.WithDefaults(ComponentKind.Trigger, result.Config.Automations[0].Triggers[0]);
        return TriggerMatcher.Matches(catalog.Find(ComponentKind.Trigger, trigger.Type)!, trigger, e, new EventFacts(true, true));
    }

    [Fact]
    public void Plugin_trigger_matches_its_event_name_only()
    {
        Assert.True(Matches("type: acme.usb.connected", UsbEvent("acme.usb.connected")));
        Assert.True(Matches("type: acme.usb.connected", UsbEvent("ACME.USB.Connected")));
        Assert.False(Matches("type: acme.usb.connected", UsbEvent("acme.usb.disconnected")));
        Assert.False(Matches("type: acme.usb.connected", Session(SystemEventKind.Unlock)));
    }

    [Fact]
    public void Plugin_trigger_fields_filter_on_event_data()
    {
        const string drive = "{ type: acme.usb.connected, drive: \"E:\" }";
        Assert.True(Matches(drive, UsbEvent("acme.usb.connected", ("drive", "e:"))));
        Assert.False(Matches(drive, UsbEvent("acme.usb.connected", ("drive", "F:"))));
        Assert.False(Matches(drive, UsbEvent("acme.usb.connected")));

        const string labels = "{ type: acme.usb.connected, labels: [BACKUP*, PHOTOS] }";
        Assert.True(Matches(labels, UsbEvent("acme.usb.connected", ("labels", "backup-2026"))));
        Assert.True(Matches(labels, UsbEvent("acme.usb.connected", ("labels", "Photos"))));
        Assert.False(Matches(labels, UsbEvent("acme.usb.connected", ("labels", "music"))));

        const string removable = "{ type: acme.usb.connected, removable: true }";
        Assert.True(Matches(removable, UsbEvent("acme.usb.connected", ("removable", "True"))));
        Assert.False(Matches(removable, UsbEvent("acme.usb.connected", ("removable", "false"))));
    }

    [Fact]
    public async Task Profile_applied_by_a_plugin_event_is_reverted_by_its_opposite_event()
    {
        var volume = new FakeVolume();
        var catalog = Catalog();
        var engine = new RuleEngine(ExecutionScope.User, new HandlerRegistry().Add(volume), new ActivityLog(), catalog: catalog, time: new ManualTime())
        {
            CurrentUser = Samet,
        };
        var result = ConfigLoader.Load("""
            version: 1
            automations:
              - id: backup
                triggers: [acme.usb.connected]
                actions:
                  - type: profile.apply
                    profile: quiet
            profiles:
              - id: quiet
                actions:
                  - type: audio.volume
                    level: 10
            """, ExecutionScope.User, catalog);
        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        engine.UpdateConfig(result.Config);

        await engine.SendAsync(UsbEvent("acme.usb.connected"));
        Assert.Equal(10, volume.Level);
        Assert.True(engine.IsProfileActive("quiet"));

        await engine.SendAsync(Session(SystemEventKind.Lock));
        Assert.True(engine.IsProfileActive("quiet"));

        await engine.SendAsync(UsbEvent("acme.usb.disconnected"));
        Assert.Equal(50, volume.Level);
        Assert.False(engine.IsProfileActive("quiet"));
    }

    [Fact]
    public void Event_data_is_available_as_placeholders()
    {
        var context = new ActionContext(UsbEvent("acme.usb.connected", ("drive", "E:")), "a", "backup", new ActivityLog());
        var values = Placeholders.ValuesFor(context, DateTimeOffset.Now);

        Assert.Equal("E:", values["event.data.drive"]);
        Assert.Equal("acme.usb.connected", values["event"]);
        Assert.Equal("Copy to E:", Placeholders.Expand("Copy to {{ event.data.drive }}", values));
    }

    [Fact]
    public async Task Automation_using_a_missing_plugin_is_kept_but_does_not_run()
    {
        const string yaml = """
            version: 1
            automations:
              - id: backup
                triggers: [unlock]
                actions:
                  - type: acme.usb.eject
                    drive: "E:"
              - id: other
                triggers: [unlock]
                actions:
                  - type: notify
                    message: hi
            """;
        var result = ConfigLoader.Load(yaml, ExecutionScope.User);

        Assert.False(result.HasErrors, string.Join("\n", result.Errors));
        Assert.Contains(result.Warnings, w => w.Message.Contains("'acme.usb'"));
        var backup = result.Config.FindAutomation("backup")!;
        Assert.True(backup.IsBlocked);
        Assert.Equal(new[] { "acme.usb" }, backup.MissingPlugins);
        Assert.False(result.Config.FindAutomation("other")!.IsBlocked);

        var notify = new RecordingAction("notify");
        var eject = new RecordingAction("acme.usb.eject");
        var engine = new RuleEngine(ExecutionScope.User, new HandlerRegistry().Add(notify).Add(eject), new ActivityLog(), time: new ManualTime())
        {
            CurrentUser = Samet,
        };
        engine.UpdateConfig(result.Config);
        await engine.SendAsync(Session(SystemEventKind.Unlock));
        Assert.Single(notify.Calls);
        Assert.Empty(eject.Calls);
        Assert.False(await engine.RunAutomationAsync("backup", checkConditions: false));
    }

    [Fact]
    public void Missing_plugin_in_a_single_component_is_an_error()
    {
        var issues = new ConfigValidator().NormalizeComponent(ComponentKind.Action, new ComponentConfig("acme.usb.eject"), ExecutionScope.User);

        var error = Assert.Single(issues, i => i.Severity == IssueSeverity.Error);
        Assert.Contains("acme.usb", error.Message);
    }

    [Fact]
    public void Unknown_built_in_looking_types_are_still_errors()
    {
        var result = ConfigLoader.Load("""
            version: 1
            automations:
              - id: a
                triggers: [unlock]
                actions:
                  - type: audio.volum
                    level: 5
            """, ExecutionScope.User);

        Assert.True(result.HasErrors);
    }

    [Theory]
    [InlineData("acme.usb.connected", "acme.usb")]
    [InlineData("acme.usb.sub.thing", "acme.usb.sub")]
    [InlineData("audio.volume", null)]
    [InlineData("notify", null)]
    [InlineData("acme..", null)]
    public void Plugin_ids_come_from_the_type(string type, string? plugin)
    {
        Assert.Equal(plugin, PluginIds.PluginOf(type));
    }
}
