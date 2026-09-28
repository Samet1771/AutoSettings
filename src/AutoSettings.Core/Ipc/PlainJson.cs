using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoSettings.Core.Config;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Ipc;

/// <summary>Converts component parameters to and from JSON for IPC.</summary>
public static class PlainJson
{
    /// <summary>Serializes parameters (typed or raw values) to a JSON object.</summary>
    public static string Serialize(IReadOnlyDictionary<string, object?> parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in parameters)
            {
                writer.WritePropertyName(key);
                WriteValue(writer, value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Deserializes a JSON object into raw parameter values (strings, numbers, booleans, lists, maps).</summary>
    public static Dictionary<string, object?> Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            result[property.Name] = ReadValue(property.Value);
        return result;
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case long l: writer.WriteNumberValue(l); break;
            case int i: writer.WriteNumberValue(i); break;
            case double d: writer.WriteNumberValue(d); break;
            case TimeSpan t: writer.WriteStringValue(ValueConverter.FormatDuration(t)); break;
            case TimeOnly t: writer.WriteStringValue(ValueConverter.FormatTime(t)); break;
            case IEnumerable<ComponentConfig> components:
                writer.WriteStartArray();
                foreach (var component in components)
                {
                    var map = new Dictionary<string, object?>(component.Parameters) { ["type"] = component.Type };
                    writer.WriteRawValue(Serialize(map));
                }
                writer.WriteEndArray();
                break;
            case IDictionary<string, object?> map:
                writer.WriteRawValue(Serialize(map.ToDictionary(p => p.Key, p => p.Value)));
                break;
            case System.Collections.IEnumerable items:
                writer.WriteStartArray();
                foreach (var item in items)
                    WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            case IFormattable f: writer.WriteStringValue(f.ToString(null, CultureInfo.InvariantCulture)); break;
            default: writer.WriteStringValue(value.ToString()); break;
        }
    }

    private static object? ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(ReadValue).ToList(),
        JsonValueKind.Object => ReadMap(element),
        _ => null,
    };

    private static RawMap ReadMap(JsonElement element)
    {
        var map = new RawMap();
        foreach (var property in element.EnumerateObject())
            map[property.Name] = ReadValue(property.Value);
        return map;
    }
}
