using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoSettings.Sdk;

/// <summary>
/// The parameters of an action or condition, as written in the automation (for example <c>drive: "E:"</c>).
/// Values have already been checked against the fields the component declares, so a required field is always there
/// and has the declared type.
/// </summary>
/// <remarks>
/// Values arrive as JSON: text, numbers, <c>true</c>/<c>false</c> and lists of text. Durations are text such as
/// <c>30s</c>, <c>1h30m</c> or <c>500ms</c>; times of day are text such as <c>22:00</c>.
/// </remarks>
public sealed partial class Parameters
{
    private readonly Dictionary<string, JsonElement> _values;

    /// <summary>Creates parameters from JSON values.</summary>
    /// <param name="values">The values by field name.</param>
    public Parameters(IReadOnlyDictionary<string, JsonElement> values) =>
        _values = new Dictionary<string, JsonElement>(values, StringComparer.OrdinalIgnoreCase);

    /// <summary>No parameters.</summary>
    public static Parameters Empty { get; } = new(new Dictionary<string, JsonElement>());

    /// <summary>Creates parameters from a JSON object, for example <c>{"drive":"E:","force":true}</c>.</summary>
    /// <param name="json">A JSON object.</param>
    /// <returns>The parameters.</returns>
    public static Parameters FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
                values[property.Name] = property.Value.Clone();
        }
        return new Parameters(values);
    }

    /// <summary>Creates parameters from plain values (text, numbers, booleans, lists of text), for tests.</summary>
    /// <param name="values">The values by field name.</param>
    /// <returns>The parameters.</returns>
    public static Parameters From(IReadOnlyDictionary<string, object?> values) =>
        FromJson(JsonSerializer.Serialize(values));

    /// <summary>The names of the parameters that are set.</summary>
    public IEnumerable<string> Names => _values.Keys;

    /// <summary>Whether <paramref name="name"/> is set (and not <c>null</c>).</summary>
    /// <param name="name">Field name.</param>
    public bool Has(string name) => _values.TryGetValue(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    /// <summary>The raw JSON value, or <c>null</c> when not set.</summary>
    /// <param name="name">Field name.</param>
    public JsonElement? GetRaw(string name) => Has(name) ? _values[name] : null;

    /// <summary>A text value (numbers and booleans are returned as text), or <c>null</c>.</summary>
    /// <param name="name">Field name.</param>
    public string? GetString(string name) => GetRaw(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } v => v.GetString(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        { ValueKind: JsonValueKind.Array } v => string.Join(", ", v.EnumerateArray().Select(e => e.ToString())),
        { } v => v.GetRawText(),
    };

    /// <summary>A whole number, or <c>null</c> when not set or not a number.</summary>
    /// <param name="name">Field name.</param>
    public long? GetInteger(string name) => GetRaw(name) switch
    {
        { ValueKind: JsonValueKind.Number } v when v.TryGetInt64(out var l) => l,
        { ValueKind: JsonValueKind.Number } v => (long)Math.Round(v.GetDouble()),
        { ValueKind: JsonValueKind.String } v when long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
        _ => null,
    };

    /// <summary>A number, or <c>null</c> when not set or not a number.</summary>
    /// <param name="name">Field name.</param>
    public double? GetNumber(string name) => GetRaw(name) switch
    {
        { ValueKind: JsonValueKind.Number } v => v.GetDouble(),
        { ValueKind: JsonValueKind.String } v when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    /// <summary>A yes/no value, or <c>null</c> when not set.</summary>
    /// <param name="name">Field name.</param>
    public bool? GetBoolean(string name) => GetRaw(name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        { ValueKind: JsonValueKind.String } v when bool.TryParse(v.GetString(), out var b) => b,
        _ => null,
    };

    /// <summary>A list of text. A single text value is returned as a list of one; not set gives an empty list.</summary>
    /// <param name="name">Field name.</param>
    public IReadOnlyList<string> GetStringList(string name) => GetRaw(name) switch
    {
        null => [],
        { ValueKind: JsonValueKind.Array } v => v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString()).ToList(),
        { } v => [GetString(name) ?? ""],
    };

    /// <summary>A duration (<c>30s</c>, <c>1h30m</c>, <c>500ms</c>, or <c>hh:mm:ss</c>), or <c>null</c>.</summary>
    /// <param name="name">Field name.</param>
    public TimeSpan? GetDuration(string name) => ParseDuration(GetString(name));

    /// <summary>A time of day (<c>22:00</c> or <c>22:00:30</c>), or <c>null</c>.</summary>
    /// <param name="name">Field name.</param>
    public TimeOnly? GetTime(string name) =>
        TimeOnly.TryParse(GetString(name), CultureInfo.InvariantCulture, out var t) ? t : null;

    /// <summary>Parses a duration like <c>1h30m</c>, <c>45s</c>, <c>500ms</c>, <c>2d</c> or <c>01:30:00</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The duration, or <c>null</c> when the text is not a duration.</returns>
    public static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (text.Contains(':') && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span))
            return span;
        var total = TimeSpan.Zero;
        var consumed = 0;
        foreach (Match m in DurationPart().Matches(text))
        {
            var amount = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            total += m.Groups[2].Value.ToLowerInvariant() switch
            {
                "ms" => TimeSpan.FromMilliseconds(amount),
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromDays(amount),
            };
            consumed += m.Length;
        }
        return consumed > 0 && consumed == text.Length ? total : null;
    }

    [GeneratedRegex(@"\G\s*(\d+(?:\.\d+)?)\s*(ms|s|m|h|d)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPart();
}
