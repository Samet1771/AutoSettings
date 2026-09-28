using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Editing;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Tests;

public class EditingTests
{
    private const string Source = """
        version: 1
        automations:
          - id: gaming
            name: Gaming
            triggers:
              - type: app_started
                app: steam.exe
            conditions:
              - type: or
                conditions:
                  - type: profile_active
                    profile: quiet
                    active: false
            actions:
              - type: profile.apply
                profile: gaming
          - id: other
            triggers: [lock]
            actions:
              - type: notify
                message: hi
        profiles:
          - id: gaming
            actions:
              - type: audio.volume
                level: 20
          - id: quiet
            actions:
              - type: audio.mute
                state: mute
          - id: unused
            actions:
              - type: audio.mute
                state: unmute
        """;

    [Fact]
    public void Duplicate_move_toggle_and_remove()
    {
        var document = new ConfigDocument(TestSupport.LoadValid(Source), ExecutionScope.User);

        var copy = document.DuplicateAutomation("gaming")!;
        Assert.Equal("gaming-2", copy.Id);
        Assert.Equal("Gaming (copy)", copy.Name);
        Assert.Equal(new[] { "gaming", "gaming-2", "other" }, document.Config.Automations.Select(a => a.Id));

        copy.Triggers[0].Parameters["app"] = new List<string> { "changed.exe" };
        Assert.Equal(new[] { "steam.exe" }, document.Config.Automations[0].Triggers[0].GetStringList("app"));

        Assert.True(document.MoveAutomation("other", -2));
        Assert.Equal("other", document.Config.Automations[0].Id);
        Assert.False(document.MoveAutomation("other", -1));

        Assert.True(document.SetEnabled("other", false));
        Assert.True(document.RemoveAutomation("gaming-2"));
        Assert.Empty(document.Validate().Where(i => i.Severity == IssueSeverity.Error));

        var reloaded = TestSupport.LoadValid(document.ToYaml());
        Assert.False(reloaded.FindAutomation("other")!.Enabled);
    }

    [Fact]
    public void Editing_does_not_change_the_original()
    {
        var original = TestSupport.LoadValid(Source);
        var document = new ConfigDocument(original, ExecutionScope.User);
        document.RemoveProfile("unused");
        Assert.Equal(3, original.Profiles.Count);
    }

    [Fact]
    public void Automation_snippet_round_trips_and_is_validated_in_context()
    {
        var document = new ConfigDocument(TestSupport.LoadValid(Source), ExecutionScope.User);
        var yaml = YamlConfigWriter.WriteAutomation(document.Config.Automations[0]);

        var (parsed, issues) = document.ParseAutomation(yaml, "gaming");
        Assert.Empty(issues);
        Assert.Equal("gaming", parsed!.Id);

        var (_, broken) = document.ParseAutomation(yaml.Replace("profile: gaming", "profile: nope"), "gaming");
        var error = Assert.Single(broken);
        Assert.Contains("no profile with the id 'nope'", error.Message);
        Assert.NotNull(error.Location);

        var (_, clash) = document.ParseAutomation(yaml, originalId: null);
        Assert.Contains(clash, i => i.Message.Contains("Another automation already uses the id 'gaming'"));

        var (_, syntax) = document.ParseAutomation("id: [x", "gaming");
        Assert.StartsWith("YAML syntax error", Assert.Single(syntax).Message);
    }

    [Fact]
    public void Profile_snippet_is_validated()
    {
        var document = new ConfigDocument(TestSupport.LoadValid(Source), ExecutionScope.User);
        var (profile, issues) = document.ParseProfile("""
            name: New
            actions:
              - type: audio.volume
                level: 500
            """, null);

        Assert.Equal("new", profile!.Id);
        Assert.Contains(issues, i => i.Message.Contains("must be at most 100"));
    }

    [Fact]
    public void Merge_renames_clashing_ids_and_references()
    {
        var target = TestSupport.LoadValid(Source);
        var source = TestSupport.LoadValid(Source);

        var result = ConfigMerge.Merge(target, source);

        Assert.Equal(new[] { "gaming-2", "other-2" }, result.AutomationIds);
        Assert.Equal(new[] { "gaming-2", "quiet-2", "unused-2" }, result.ProfileIds);
        var merged = target.FindAutomation("gaming-2")!;
        Assert.Equal("gaming-2", merged.Actions[0].GetString("profile"));
        Assert.Equal("quiet-2", merged.Conditions[0].GetComponents("conditions")[0].GetString("profile"));
        Assert.Empty(new ConfigValidator().Validate(target, ExecutionScope.User).Where(i => i.Severity == IssueSeverity.Error));
    }

    [Fact]
    public void Export_includes_referenced_profiles()
    {
        var config = TestSupport.LoadValid(Source);

        var exported = ConfigMerge.Export(config, ["gaming"], []);

        Assert.Equal(new[] { "gaming" }, exported.Automations.Select(a => a.Id));
        Assert.Equal(new[] { "gaming", "quiet" }, exported.Profiles.Select(p => p.Id));
        TestSupport.LoadValid(YamlConfigWriter.Write(exported));
    }

    public static TheoryData<string> TemplateIds()
    {
        var data = new TheoryData<string>();
        foreach (var template in Templates.All)
            data.Add(template.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Templates_are_valid_and_merge_cleanly(string id)
    {
        var template = Templates.Find(id)!;
        var result = ConfigLoader.Load(template.Yaml, ExecutionScope.User);
        Assert.True(result.Issues.Count == 0, string.Join("\n", result.Issues));

        var target = TestSupport.LoadValid(template.Yaml);
        ConfigMerge.Merge(target, template.Load());
        Assert.Empty(new ConfigValidator().Validate(target, ExecutionScope.User).Where(i => i.Severity == IssueSeverity.Error));
    }

    [Fact]
    public void Field_text_parsing()
    {
        var apps = ComponentCatalog.Default.Find(ComponentKind.Trigger, "app_started")!.Field("app")!;
        Assert.Equal(new List<string> { "a.exe", "b.exe" }, FieldText.Parse(apps, " a.exe, b.exe ,"));
        Assert.Null(FieldText.Parse(apps, "  "));
        Assert.Equal("30s", FieldText.Format(TimeSpan.FromSeconds(30)));

        var component = new ComponentConfig("audio.volume", new Dictionary<string, object?> { ["level"] = "150" });
        Assert.Contains(FieldText.Validate(ComponentKind.Action, component, ExecutionScope.User), m => m.Contains("must be at most 100"));
        Assert.Equal("150", component.Parameters["level"]);
    }
}
