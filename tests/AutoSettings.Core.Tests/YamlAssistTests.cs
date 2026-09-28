using AutoSettings.Core.Catalog;
using AutoSettings.Core.Editing;

namespace AutoSettings.Core.Tests;

public class YamlAssistTests
{
    private const string Caret = "|";

    private static CompletionResult Complete(string textWithCaret, ExecutionScope scope = ExecutionScope.User,
        YamlDocumentKind kind = YamlDocumentKind.File, IEnumerable<string>? profiles = null)
    {
        var offset = textWithCaret.IndexOf(Caret, StringComparison.Ordinal);
        var text = textWithCaret.Remove(offset, 1);
        return YamlAssist.Complete(text, offset, scope, kind, profiles);
    }

    private static List<string> Labels(CompletionResult result) => result.Items.Select(i => i.Label).ToList();

    [Fact]
    public void Suggests_trigger_types_for_the_trigger_list()
    {
        var result = Complete("""
            version: 1
            automations:
              - id: a
                triggers:
                  - type: app_f|
            """);

        Assert.Equal("app_focused", Labels(result)[0]);
        Assert.DoesNotContain("audio.volume", Labels(result));
    }

    [Fact]
    public void Suggests_action_types_filtered_by_scope()
    {
        const string text = """
            automations:
              - id: a
                actions:
                  - type: serv|
            """;
        Assert.Empty(Labels(Complete(text, ExecutionScope.User)));
        Assert.Equal(new[] { "service.control" }, Labels(Complete(text, ExecutionScope.Machine)));
    }

    [Fact]
    public void Suggests_missing_fields_of_the_component()
    {
        var labels = Labels(Complete("""
            automations:
              - id: a
                triggers:
                  - type: app_started
                    app: steam.exe
                    |
            """));

        Assert.Contains("instance", labels);
        Assert.Contains("user", labels);
        Assert.DoesNotContain("app", labels);
        Assert.DoesNotContain("type", labels);
    }

    [Fact]
    public void Suggests_type_as_the_first_key_of_a_new_list_item()
    {
        var labels = Labels(Complete("""
            automations:
              - id: a
                conditions:
                  - |
            """));

        Assert.Equal(new[] { "type" }, labels);
    }

    [Fact]
    public void Suggests_automation_keys()
    {
        var labels = Labels(Complete("""
            automations:
              - id: a
                name: A
                |
              - id: b
            """));

        Assert.Contains("triggers", labels);
        Assert.Contains("cooldown", labels);
        Assert.DoesNotContain("id", labels);
        Assert.DoesNotContain("name", labels);
    }

    [Fact]
    public void Suggests_enum_and_boolean_values()
    {
        Assert.Equal(new[] { "light", "dark", "toggle" }, Labels(Complete("""
            actions:
              - type: theme.mode
                mode: |
            """, kind: YamlDocumentKind.Automation)));

        Assert.Equal(new[] { "false" }, Labels(Complete("""
            id: a
            enabled: f|
            """, kind: YamlDocumentKind.Automation)));
    }

    [Fact]
    public void Suggests_profile_ids()
    {
        Assert.Equal(new[] { "gaming", "quiet" }, Labels(Complete("""
            automations:
              - id: a
                actions:
                  - type: profile.apply
                    profile: |
            profiles:
              - id: gaming
                actions: []
              - id: quiet
                actions: []
            """)));

        Assert.Equal(new[] { "work" }, Labels(Complete("""
            actions:
              - type: profile.revert
                profile: |
            """, kind: YamlDocumentKind.Automation, profiles: ["work"])));
    }

    [Fact]
    public void Nested_conditions_suggest_condition_types()
    {
        var labels = Labels(Complete("""
            conditions:
              - type: or
                conditions:
                  - type: powe|
            """, kind: YamlDocumentKind.Automation));

        Assert.Equal(new[] { "power_source" }, labels);
    }

    [Fact]
    public void Replace_start_covers_the_typed_word()
    {
        const string text = "actions:\n  - type: aud";
        var result = YamlAssist.Complete(text, text.Length, ExecutionScope.User, YamlDocumentKind.Automation);
        Assert.Equal(text.Length - 3, result.ReplaceStart);
    }

    [Fact]
    public void Hover_explains_types_and_fields()
    {
        const string text = """
            triggers:
              - type: app_focused
                title: "*YouTube*"
            """;

        var type = YamlAssist.Hover(text, text.IndexOf("app_focused", StringComparison.Ordinal) + 2, YamlDocumentKind.Automation);
        Assert.Equal("App focused (app_focused)", type!.Title);

        var field = YamlAssist.Hover(text, text.IndexOf("title", StringComparison.Ordinal) + 1, YamlDocumentKind.Automation);
        Assert.Equal("title", field!.Title);
        Assert.Contains("window title", field.Description);

        var key = YamlAssist.Hover(text, 2, YamlDocumentKind.Automation);
        Assert.Equal("triggers", key!.Title);
    }
}
