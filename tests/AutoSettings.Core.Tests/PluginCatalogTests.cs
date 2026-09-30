using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using static AutoSettings.Core.Tests.TestSupport;

namespace AutoSettings.Core.Tests;

public class PluginCatalogTests
{
    private static readonly ComponentSource Demo = ComponentSource.Plugin("acme.demo", "1.0.0", ExecutionScope.User);

    private static ComponentDescriptor Action(string type) => new()
    {
        Type = type,
        Kind = ComponentKind.Action,
        Category = "Demo",
        Title = "Say something",
        Description = "Test action.",
        Fields = [Fields.Text("text", "What to say.")],
    };

    private static ComponentDescriptor UnlockTrigger(string type) => new()
    {
        Type = type,
        Kind = ComponentKind.Trigger,
        Category = "Demo",
        Title = "Unlocked (plugin)",
        Description = "Test trigger.",
        EventKind = SystemEventKind.Unlock,
    };

    [Fact]
    public void Compose_adds_plugin_components_with_their_source()
    {
        var result = ComponentCatalog.Compose(BuiltInComponents.All, [new PluginContribution(Demo, [Action("acme.demo.say")])]);

        Assert.Empty(result.Rejected);
        var say = result.Catalog.Find(ComponentKind.Action, "acme.demo.say");
        Assert.NotNull(say);
        Assert.Equal("acme.demo", say.Source.PluginId);
        Assert.False(say.Source.IsBuiltIn);
        Assert.NotNull(say.Field(ComponentCatalog.ContinueOnError.Name));
        Assert.True(result.Catalog.Find(ComponentKind.Action, "notify")!.Source.IsBuiltIn);
    }

    [Fact]
    public void A_plugin_that_reuses_a_type_is_left_out_whole()
    {
        var other = ComponentSource.Plugin("acme.other", "1.0.0", ExecutionScope.User);
        var result = ComponentCatalog.Compose(BuiltInComponents.All,
        [
            new PluginContribution(Demo, [Action("acme.demo.say"), Action("Notify")]),
            new PluginContribution(other, [Action("acme.other.go")]),
        ]);

        var rejected = Assert.Single(result.Rejected);
        Assert.Equal("acme.demo", rejected.PluginId);
        Assert.Contains("Notify", rejected.Reason);
        Assert.Null(result.Catalog.Find(ComponentKind.Action, "acme.demo.say"));
        Assert.NotNull(result.Catalog.Find(ComponentKind.Action, "acme.other.go"));
        Assert.True(result.Catalog.Find(ComponentKind.Action, "notify")!.Source.IsBuiltIn);
    }

    [Fact]
    public void A_plugin_with_the_same_type_twice_is_left_out()
    {
        var result = ComponentCatalog.Compose(BuiltInComponents.All, [new PluginContribution(Demo, [Action("acme.demo.say"), Action("acme.demo.say")])]);

        Assert.Single(result.Rejected);
        Assert.Equal(ComponentCatalog.BuiltIn.All.Count, result.Catalog.All.Count);
    }

    [Fact]
    public void Plugin_components_validate_only_with_a_catalog_that_has_them()
    {
        const string yaml = """
            version: 1
            automations:
              - id: a
                triggers: [unlock]
                actions:
                  - type: acme.demo.say
                    text: hi
            """;
        var catalog = ComponentCatalog.Compose(BuiltInComponents.All, [new PluginContribution(Demo, [Action("acme.demo.say")])]).Catalog;

        Assert.True(ConfigLoader.Load(yaml, ExecutionScope.User).HasErrors);
        Assert.False(ConfigLoader.Load(yaml, ExecutionScope.User, catalog).HasErrors);
    }

    [Fact]
    public async Task Engine_picks_up_plugin_triggers_after_UseCatalog()
    {
        var catalog = ComponentCatalog.Compose(BuiltInComponents.All,
            [new PluginContribution(Demo, [UnlockTrigger("acme.demo.unlocked"), Action("acme.demo.say")])]).Catalog;
        var say = new RecordingAction("acme.demo.say");
        var engine = new RuleEngine(ExecutionScope.User, new HandlerRegistry().Add(say), new ActivityLog(), time: new ManualTime())
        {
            CurrentUser = Samet,
        };
        var config = ConfigLoader.Load("""
            version: 1
            automations:
              - id: a
                triggers: [acme.demo.unlocked]
                actions:
                  - type: acme.demo.say
                    text: hi
            """, ExecutionScope.User, catalog);
        Assert.False(config.HasErrors, string.Join("\n", config.Errors));
        engine.UpdateConfig(config.Config);

        await engine.SendAsync(Session(SystemEventKind.Unlock));
        Assert.Empty(say.Calls);

        engine.UseCatalog(catalog);
        await engine.SendAsync(Session(SystemEventKind.Unlock));
        Assert.Equal("hi", Assert.Single(say.Calls).GetString("text"));
    }

    [Fact]
    public void Removed_handlers_are_no_longer_found()
    {
        var registry = new HandlerRegistry().Add(new RecordingAction("acme.demo.say"));

        Assert.True(registry.RemoveAction("ACME.demo.say"));
        Assert.Null(registry.FindAction("acme.demo.say"));
        Assert.False(registry.RemoveAction("acme.demo.say"));
    }

    [Fact]
    public void Provider_raises_Changed_with_the_new_catalog()
    {
        var provider = new ComponentCatalogProvider();
        var catalog = ComponentCatalog.Compose(BuiltInComponents.All, [new PluginContribution(Demo, [Action("acme.demo.say")])]).Catalog;
        ComponentCatalog? seen = null;
        provider.Changed += (_, _) => seen = provider.Current;

        Assert.Same(ComponentCatalog.BuiltIn, provider.Current);
        provider.Update(catalog);
        Assert.Same(catalog, seen);
    }
}
