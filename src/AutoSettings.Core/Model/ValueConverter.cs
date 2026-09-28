using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoSettings.Core.Catalog;

namespace AutoSettings.Core.Model;

/// <summary>
/// Converts raw YAML values (mostly strings) into the typed values described by a
/// <see cref="FieldDescriptor"/>, and formats typed values back to text.
/// </summary>
public static partial class ValueConverter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] TimeFormats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"];

    /// <summary>Converts <paramref name="raw"/> to the type required by <paramref name="field"/>.</summary>
    /// <returns><c>false</c> with a user-facing <paramref name="error"/> when the value is invalid.</returns>
    public static bool TryConvert(object? raw, FieldDescriptor field, out object? value, out string? error)
    {
        value = null;
        error = null;
        if (raw is null)
            return true;

        switch (field.Type)
        {
            case FieldType.String:
            case FieldType.Multiline:
            case FieldType.Path:
                if (!IsScalar(raw))
                {
                    error = "expected a single value, not a list or a map";
                    return false;
                }
                value = ToText(raw);
                return true;

            case FieldType.Integer:
                if (!TryToInteger(raw, out var integer))
                {
                    error = $"'{ToDisplay(raw)}' is not a whole number";
                    return false;
                }
                if (!CheckRange(integer, field, out error))
                    return false;
                value = integer;
                return true;

            case FieldType.Number:
                if (!TryToNumber(raw, out var number))
                {
                    error = $"'{ToDisplay(raw)}' is not a number";
                    return false;
                }
                if (!CheckRange(number, field, out error))
                    return false;
                value = number;
                return true;

            case FieldType.Boolean:
                if (!TryToBoolean(raw, out var boolean))
                {
                    error = $"'{ToDisplay(raw)}' is not true or false";
                    return false;
                }
                value = boolean;
                return true;

            case FieldType.Enum:
            {
                if (!IsScalar(raw))
                {
                    error = "expected a single value, not a list or a map";
                    return false;
                }
                var text = ToText(raw)!;
                var match = field.AllowedValues?.FirstOrDefault(a => string.Equals(a, text, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    error = $"'{text}' is not one of: {string.Join(", ", field.AllowedValues ?? [])}";
                    return false;
                }
                value = match;
                return true;
            }

            case FieldType.Duration:
                if (!TryToDuration(raw, out var duration))
                {
                    error = $"'{ToDisplay(raw)}' is not a duration (examples: 30s, 5m, 1h30m, 00:05:00)";
                    return false;
                }
                value = duration;
                return true;

            case FieldType.Time:
                if (!TryToTime(raw, out var time))
                {
                    error = $"'{ToDisplay(raw)}' is not a time of day (examples: 07:30, 22:00)";
                    return false;
                }
                value = time;
                return true;

            case FieldType.StringList:
            case FieldType.AppList:
            case FieldType.UserList:
            {
                if (!TryToStringList(raw, out var list))
                {
                    error = "expected a value or a list of values";
                    return false;
                }
                if (field.AllowedValues is { Count: > 0 } allowed)
                {
                    var normalized = new List<string>(list.Count);
                    foreach (var item in list)
                    {
                        var match = allowed.FirstOrDefault(a => string.Equals(a, item, StringComparison.OrdinalIgnoreCase));
                        if (match is null)
                        {
                            error = $"'{item}' is not one of: {string.Join(", ", allowed)}";
                            return false;
                        }
                        normalized.Add(match);
                    }
                    list = normalized;
                }
                value = list;
                return true;
            }

            case FieldType.ConditionList:
                if (raw is IEnumerable<ComponentConfig> components)
                {
                    value = components.ToList();
                    return true;
                }
                error = "expected a list of conditions";
                return false;

            default:
                error = $"unsupported field type {field.Type}";
                return false;
        }
    }

    private static bool CheckRange(double value, FieldDescriptor field, out string? error)
    {
        error = null;
        if (field.Minimum is { } min && value < min)
            error = $"must be at least {min.ToString(Invariant)}";
        else if (field.Maximum is { } max && value > max)
            error = $"must be at most {max.ToString(Invariant)}";
        return error is null;
    }

    /// <summary>Whether <paramref name="value"/> is a single value rather than a list or map.</summary>
    public static bool IsScalar(object? value) =>
        value is string or bool or long or int or short or byte or double or float or decimal or TimeSpan or TimeOnly;

    /// <summary>Formats a value as plain text (lists are comma-separated).</summary>
    public static string? ToText(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        TimeSpan t => FormatDuration(t),
        TimeOnly t => FormatTime(t),
        IFormattable f => f.ToString(null, Invariant),
        IEnumerable<string> list => string.Join(", ", list),
        _ => value.ToString(),
    };

    /// <summary>Formats a value for logs and messages.</summary>
    public static string ToDisplay(object? value) => value switch
    {
        null => "(none)",
        string s => s,
        IEnumerable<string> list => "[" + string.Join(", ", list) + "]",
        IEnumerable<ComponentConfig> components => $"[{components.Count()} item(s)]",
        System.Collections.IEnumerable and not string => "[...]",
        _ => ToText(value) ?? "",
    };

    /// <summary>Converts to a whole number.</summary>
    public static bool TryToInteger(object? value, out long result)
    {
        switch (value)
        {
            case long l: result = l; return true;
            case int i: result = i; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            case double d when d % 1 == 0 && d >= long.MinValue && d <= long.MaxValue:
                result = (long)d; return true;
            case string s when long.TryParse(s.Trim(), NumberStyles.Integer, Invariant, out var parsed):
                result = parsed; return true;
            default: result = 0; return false;
        }
    }

    /// <summary>Converts to a floating-point number.</summary>
    public static bool TryToNumber(object? value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case decimal m: result = (double)m; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case string s when double.TryParse(s.Trim(), NumberStyles.Float, Invariant, out var parsed):
                result = parsed; return true;
            default: result = 0; return false;
        }
    }

    /// <summary>Converts to a boolean. Accepts true/false, yes/no, on/off and 1/0.</summary>
    public static bool TryToBoolean(object? value, out bool result)
    {
        switch (value)
        {
            case bool b: result = b; return true;
            case long l when l is 0 or 1: result = l == 1; return true;
            case int i when i is 0 or 1: result = i == 1; return true;
            case string s:
                switch (s.Trim().ToLowerInvariant())
                {
                    case "true": case "yes": case "on": case "1": result = true; return true;
                    case "false": case "no": case "off": case "0": result = false; return true;
                }
                break;
        }
        result = false;
        return false;
    }

    /// <summary>Converts to a duration. Numbers are seconds; strings may be <c>90s</c>, <c>5m</c>, <c>1h30m</c>, <c>250ms</c> or <c>00:05:00</c>.</summary>
    public static bool TryToDuration(object? value, out TimeSpan result)
    {
        switch (value)
        {
            case TimeSpan t: result = t; return t >= TimeSpan.Zero;
            case long l when l >= 0: result = TimeSpan.FromSeconds(l); return true;
            case int i when i >= 0: result = TimeSpan.FromSeconds(i); return true;
            case double d when d >= 0: result = TimeSpan.FromSeconds(d); return true;
            case string s: return TryParseDuration(s, out result);
            default: result = default; return false;
        }
    }

    /// <summary>Converts to a time of day (<c>HH:mm</c> or <c>HH:mm:ss</c>).</summary>
    public static bool TryToTime(object? value, out TimeOnly result)
    {
        switch (value)
        {
            case TimeOnly t: result = t; return true;
            case string s: return TimeOnly.TryParseExact(s.Trim(), TimeFormats, Invariant, DateTimeStyles.None, out result);
            default: result = default; return false;
        }
    }

    /// <summary>Converts a single value or a list of single values to a list of strings.</summary>
    public static bool TryToStringList(object? value, out List<string> result)
    {
        result = [];
        switch (value)
        {
            case null:
                return true;
            case string s:
                result.Add(s);
                return true;
            case IEnumerable<string> strings:
                result.AddRange(strings);
                return true;
            case IEnumerable<object?> items:
                foreach (var item in items)
                {
                    if (item is null)
                        continue;
                    if (!IsScalar(item))
                        return false;
                    result.Add(ToText(item)!);
                }
                return true;
            default:
                if (!IsScalar(value))
                    return false;
                result.Add(ToText(value)!);
                return true;
        }
    }

    /// <summary>Parses a duration string such as <c>1h30m</c>, <c>45s</c>, <c>250ms</c>, <c>2d</c>, <c>00:05:00</c> or a number of seconds.</summary>
    public static bool TryParseDuration(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();

        if (double.TryParse(text, NumberStyles.Float, Invariant, out var seconds))
        {
            if (seconds < 0)
                return false;
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (text.Contains(':'))
            return TimeSpan.TryParse(text, Invariant, out value) && value >= TimeSpan.Zero;

        var total = TimeSpan.Zero;
        var consumed = 0;
        foreach (Match m in DurationPart().Matches(text))
        {
            var amount = double.Parse(m.Groups[1].Value, Invariant);
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
        if (consumed == 0 || consumed != text.Length)
            return false;
        value = total;
        return true;
    }

    /// <summary>Formats a duration in the compact form accepted by <see cref="TryParseDuration"/>, e.g. <c>1h30m</c>.</summary>
    public static string FormatDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            return "0s";
        var sb = new StringBuilder();
        if (value.Days > 0) sb.Append(value.Days.ToString(Invariant)).Append('d');
        if (value.Hours > 0) sb.Append(value.Hours.ToString(Invariant)).Append('h');
        if (value.Minutes > 0) sb.Append(value.Minutes.ToString(Invariant)).Append('m');
        if (value.Seconds > 0) sb.Append(value.Seconds.ToString(Invariant)).Append('s');
        if (value.Milliseconds > 0) sb.Append(value.Milliseconds.ToString(Invariant)).Append("ms");
        return sb.Length == 0 ? "0s" : sb.ToString();
    }

    /// <summary>Formats a time of day as <c>HH:mm</c> (or <c>HH:mm:ss</c> when seconds are set).</summary>
    public static string FormatTime(TimeOnly value) =>
        value.ToString(value.Second == 0 ? "HH:mm" : "HH:mm:ss", Invariant);

    [GeneratedRegex(@"\G\s*(\d+(?:\.\d+)?)\s*(ms|s|m|h|d)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPart();
}
