using System.Text.Json;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Schema;

namespace AutoSettings.Core.Tests;

public class CatalogTests
{
    public static TheoryData<string, string> Descriptors()
    {
        var data = new TheoryData<string, string>();
        foreach (var d in ComponentCatalog.Default.All)
            data.Add(d.Kind.ToString(), d.Type);
        return data;
    }

    [Theory]
    [MemberData(nameof(Descriptors))]
    public void Every_component_is_documented_and_its_example_is_valid(string kind, string type)
    {
        var descriptor = ComponentCatalog.Default.Find(Enum.Parse<ComponentKind>(kind), type)!;
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Title));
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
        Assert.All(descriptor.Fields, f => Assert.False(string.IsNullOrWhiteSpace(f.Description)));
        Assert.NotNull(descriptor.Example);

        var yaml = WrapExample(descriptor);
        var user = ConfigLoader.Load(yaml, ExecutionScope.User);
        var machine = ConfigLoader.Load(yaml, ExecutionScope.Machine);
        Assert.True(!user.HasErrors || !machine.HasErrors,
            $"Example for {type} is invalid:\n{yaml}\n{string.Join("\n", user.Errors.Concat(machine.Errors))}");
        Assert.Empty(user.HasErrors ? machine.Warnings : user.Warnings);
    }

    [Fact]
    public void Every_action_gets_continue_on_error()
    {
        Assert.All(ComponentCatalog.Default.OfKind(ComponentKind.Action),
            d => Assert.NotNull(d.Field(ComponentCatalog.ContinueOnError.Name)));
    }

    [Fact]
    public void Every_trigger_maps_to_an_event()
    {
        Assert.All(ComponentCatalog.Default.OfKind(ComponentKind.Trigger), d => Assert.NotNull(d.EventKind));
    }

    [Fact]
    public void Defaults_are_valid_values()
    {
        foreach (var descriptor in ComponentCatalog.Default.All)
        {
            foreach (var field in descriptor.Fields.Where(f => f.Default is not null))
            {
                Assert.True(Model.ValueConverter.TryConvert(field.Default, field, out _, out var error),
                    $"{descriptor.Type}.{field.Name} default is invalid: {error}");
            }
        }
    }

    [Fact]
    public void Schema_is_valid_json_and_lists_every_type()
    {
        var json = JsonSchemaGenerator.Generate(ComponentCatalog.Default);
        using var document = JsonDocument.Parse(json);
        var actionTypes = document.RootElement
            .GetProperty("definitions").GetProperty("action")
            .GetProperty("properties").GetProperty("type").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(ComponentCatalog.Default.OfKind(ComponentKind.Action).Select(d => d.Type), actionTypes);
    }

    [Fact]
    public void User_schema_excludes_machine_only_components()
    {
        var json = JsonSchemaGenerator.Generate(ComponentCatalog.Default, ExecutionScope.User);
        Assert.DoesNotContain("\"boot\"", json);
        Assert.DoesNotContain("\"service.control\"", json);
        Assert.Contains("\"app_focused\"", json);
    }

    internal static string WrapExample(ComponentDescriptor descriptor)
    {
        var lines = descriptor.Example!.Replace("\r\n", "\n").TrimEnd().Split('\n');
        var item = "      - " + lines[0] + "\n" + string.Concat(lines.Skip(1).Select(l => "        " + l + "\n"));
        var profiles = """
            profiles:
              - id: gaming
                actions:
                  - type: audio.mute
                    state: mute

            """;

        return descriptor.Kind switch
        {
            ComponentKind.Trigger => "version: 1\nautomations:\n  - id: test\n    triggers:\n" + item
                + "    actions:\n      - type: notify\n        message: hi\n",
            ComponentKind.Condition => "version: 1\nautomations:\n  - id: test\n    triggers:\n      - type: logon\n    conditions:\n" + item
                + "    actions:\n      - type: notify\n        message: hi\n",
            _ => "version: 1\nautomations:\n  - id: test\n    triggers:\n      - type: logon\n    actions:\n" + item + profiles,
        };
    }
}
