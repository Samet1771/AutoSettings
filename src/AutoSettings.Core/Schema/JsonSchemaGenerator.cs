using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Schema;

/// <summary>
/// Generates a JSON Schema (draft-07) for <c>automations.yaml</c> from the component catalog.
/// YAML editors (the built-in editor, VS Code's YAML extension) use it for autocomplete,
/// hover help and validation.
/// </summary>
public static class JsonSchemaGenerator
{
    private const string DurationPattern = @"^\s*(\d+(\.\d+)?\s*(ms|s|m|h|d)\s*)+$|^\d+:\d{2}(:\d{2})?$|^\d+(\.\d+)?$";
    private const string TimePattern = @"^\d{1,2}:\d{2}(:\d{2})?$";

    /// <summary>Returns the schema as indented JSON.</summary>
    /// <param name="catalog">Catalog to describe.</param>
    /// <param name="scope">When set, only components usable in that kind of file are included.</param>
    public static string Generate(ComponentCatalog catalog, ExecutionScope? scope = null)
    {
        var root = new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["$id"] = "https://github.com/Samet1771/AutoSettings/docs/reference/automations.schema.json",
            ["title"] = $"{Product.Name} automations",
            ["description"] = $"An {Product.Name} automations.yaml file: automations (when/if/then rules) and profiles (named sets of actions).",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("version"),
            ["properties"] = new JsonObject
            {
                ["version"] = new JsonObject
                {
                    ["description"] = "File format version.",
                    ["const"] = Product.ConfigVersion,
                },
                ["automations"] = new JsonObject
                {
                    ["description"] = "Rules: when a trigger fires and all conditions are true, run the actions.",
                    ["type"] = "array",
                    ["items"] = Ref("automation"),
                },
                ["profiles"] = new JsonObject
                {
                    ["description"] = "Named sets of actions that can be applied and reverted as a unit.",
                    ["type"] = "array",
                    ["items"] = Ref("profile"),
                },
            },
            ["definitions"] = new JsonObject
            {
                ["automation"] = AutomationSchema(),
                ["profile"] = ProfileSchema(),
                ["trigger"] = ComponentSchema(catalog, ComponentKind.Trigger, scope),
                ["condition"] = ComponentSchema(catalog, ComponentKind.Condition, scope),
                ["action"] = ComponentSchema(catalog, ComponentKind.Action, scope),
            },
        };

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n") + "\n";
    }

    private static JsonObject Ref(string name) => new() { ["$ref"] = $"#/definitions/{name}" };

    private static JsonObject ListOf(string name) => new() { ["type"] = "array", ["items"] = Ref(name) };

    private static JsonObject AutomationSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("triggers", "actions"),
        ["properties"] = new JsonObject
        {
            ["id"] = Described(new JsonObject { ["type"] = "string" }, "Unique, stable id, e.g. gaming-mode. Generated from the name when missing."),
            ["name"] = Described(new JsonObject { ["type"] = "string" }, "Friendly name shown in the app and the activity log."),
            ["description"] = Described(new JsonObject { ["type"] = "string" }, "Optional notes."),
            ["enabled"] = Described(new JsonObject { ["type"] = "boolean", ["default"] = true }, "Set to false to keep the automation without running it."),
            ["cooldown"] = Described(DurationSchema(), "Minimum time between two runs, e.g. 30s. Triggers during the cooldown are ignored."),
            ["triggers"] = Described(ListOf("trigger"), "Any of these starts the automation."),
            ["conditions"] = Described(ListOf("condition"), "All of these must be true for the actions to run."),
            ["actions"] = Described(ListOf("action"), "Run in order."),
        },
    };

    private static JsonObject ProfileSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("id", "actions"),
        ["properties"] = new JsonObject
        {
            ["id"] = Described(new JsonObject { ["type"] = "string" }, "Unique id, used by profile.apply."),
            ["name"] = Described(new JsonObject { ["type"] = "string" }, "Friendly name."),
            ["description"] = Described(new JsonObject { ["type"] = "string" }, "Optional notes."),
            ["priority"] = Described(new JsonObject { ["type"] = "integer", ["default"] = 0 },
                "When active profiles change the same setting, the higher priority wins."),
            ["actions"] = Described(ListOf("action"), "The settings this profile applies."),
        },
    };

    private static JsonObject ComponentSchema(ComponentCatalog catalog, ComponentKind kind, ExecutionScope? scope)
    {
        var descriptors = catalog.OfKind(kind).Where(d => IsAvailable(d, scope)).ToList();
        var types = new JsonArray(descriptors.Select(d => (JsonNode?)JsonValue.Create(d.Type)).ToArray());
        var branches = new JsonArray();

        foreach (var descriptor in descriptors)
        {
            var properties = new JsonObject
            {
                ["type"] = new JsonObject { ["const"] = descriptor.Type, ["description"] = descriptor.Description },
            };
            foreach (var field in descriptor.Fields)
                properties[field.Name] = FieldSchema(field);

            var then = new JsonObject
            {
                ["title"] = descriptor.Title,
                ["description"] = descriptor.Description,
                ["properties"] = properties,
                ["additionalProperties"] = false,
            };
            var required = descriptor.Fields.Where(f => f.Required).Select(f => (JsonNode?)JsonValue.Create(f.Name)).ToArray();
            if (required.Length > 0)
                then["required"] = new JsonArray(required);

            branches.Add(new JsonObject
            {
                ["if"] = new JsonObject
                {
                    ["required"] = new JsonArray("type"),
                    ["properties"] = new JsonObject { ["type"] = new JsonObject { ["const"] = descriptor.Type } },
                },
                ["then"] = then,
            });
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("type"),
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject
                {
                    ["description"] = $"The {kind.ToString().ToLowerInvariant()} type.",
                    ["enum"] = types,
                },
            },
            ["allOf"] = branches,
        };
    }

    private static bool IsAvailable(ComponentDescriptor descriptor, ExecutionScope? scope) => scope switch
    {
        null => true,
        ExecutionScope.User when descriptor.Kind == ComponentKind.Action => descriptor.RunsAs == ExecutionScope.User || descriptor.RunsAsResolver is not null,
        ExecutionScope.User => descriptor.AvailableIn.HasFlag(ScopeSupport.User),
        _ => descriptor.Kind == ComponentKind.Action || descriptor.AvailableIn.HasFlag(ScopeSupport.Machine),
    };

    private static JsonObject FieldSchema(FieldDescriptor field)
    {
        JsonObject schema = field.Type switch
        {
            FieldType.String or FieldType.Multiline or FieldType.Path => new JsonObject { ["type"] = "string" },
            FieldType.Integer => new JsonObject { ["type"] = "integer" },
            FieldType.Number => new JsonObject { ["type"] = "number" },
            FieldType.Boolean => new JsonObject { ["type"] = "boolean" },
            FieldType.Enum => new JsonObject { ["enum"] = Strings(field.AllowedValues ?? []) },
            FieldType.Duration => DurationSchema(),
            FieldType.Time => new JsonObject { ["type"] = "string", ["pattern"] = TimePattern },
            FieldType.ConditionList => ListOf("condition"),
            _ => ListSchema(field.AllowedValues),
        };

        if (field.Minimum is { } min) schema["minimum"] = min;
        if (field.Maximum is { } max) schema["maximum"] = max;
        if (DefaultValue(field.Default) is { } defaultValue) schema["default"] = defaultValue;
        if (field.Example is { } example) schema["examples"] = new JsonArray(example);
        return Described(schema, field.Description);
    }

    private static JsonObject ListSchema(IReadOnlyList<string>? allowed)
    {
        JsonObject Item() => allowed is { Count: > 0 } ? new JsonObject { ["enum"] = Strings(allowed) } : new JsonObject { ["type"] = "string" };
        return new JsonObject
        {
            ["anyOf"] = new JsonArray(Item(), new JsonObject { ["type"] = "array", ["items"] = Item() }),
        };
    }

    private static JsonObject DurationSchema() => new()
    {
        ["anyOf"] = new JsonArray(
            new JsonObject { ["type"] = "string", ["pattern"] = DurationPattern },
            new JsonObject { ["type"] = "number", ["minimum"] = 0 }),
    };

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private static JsonNode? DefaultValue(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        long l => JsonValue.Create(l),
        int i => JsonValue.Create(i),
        double d => JsonValue.Create(d),
        _ => JsonValue.Create(ValueConverter.ToText(value)),
    };

    private static JsonObject Described(JsonObject schema, string description)
    {
        schema["description"] = description;
        return schema;
    }
}
