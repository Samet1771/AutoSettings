namespace AutoSettings.Core.Catalog;

/// <summary>The value type of a component field. Drives validation, the editor widgets and the docs.</summary>
public enum FieldType
{
    /// <summary>Single-line text.</summary>
    String,
    /// <summary>Multi-line text such as a script.</summary>
    Multiline,
    /// <summary>A file or folder path (the editor shows a file picker).</summary>
    Path,
    /// <summary>A whole number.</summary>
    Integer,
    /// <summary>A number that may have decimals.</summary>
    Number,
    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,
    /// <summary>One of <see cref="FieldDescriptor.AllowedValues"/>.</summary>
    Enum,
    /// <summary>A duration such as <c>30s</c>, <c>5m</c> or <c>1h30m</c>.</summary>
    Duration,
    /// <summary>A time of day such as <c>22:00</c>.</summary>
    Time,
    /// <summary>A value or a list of values.</summary>
    StringList,
    /// <summary>One or more app patterns (exe name, path or wildcard). The editor shows an app picker.</summary>
    AppList,
    /// <summary>One or more user names, <c>DOMAIN\name</c> or SIDs. The editor shows a user picker.</summary>
    UserList,
    /// <summary>A nested list of conditions (for <c>and</c>, <c>or</c>, <c>not</c>).</summary>
    ConditionList,
}

/// <summary>Describes one parameter of a trigger, condition or action.</summary>
/// <param name="Name">Field name as written in YAML (snake_case).</param>
/// <param name="Type">Value type.</param>
/// <param name="Description">One or two sentences for the editor tooltip and the docs.</param>
public sealed record FieldDescriptor(string Name, FieldType Type, string Description)
{
    /// <summary>Whether the field must be set.</summary>
    public bool Required { get; init; }

    /// <summary>Value used when the field is not set (raw or typed).</summary>
    public object? Default { get; init; }

    /// <summary>Allowed values for <see cref="FieldType.Enum"/> (and optionally lists).</summary>
    public IReadOnlyList<string>? AllowedValues { get; init; }

    /// <summary>Smallest allowed number.</summary>
    public double? Minimum { get; init; }

    /// <summary>Largest allowed number.</summary>
    public double? Maximum { get; init; }

    /// <summary>Example value for docs and placeholders in the editor.</summary>
    public string? Example { get; init; }

    /// <summary>Whether <c>{{ placeholders }}</c> are replaced in this field before the action runs.</summary>
    public bool SupportsPlaceholders => Type is FieldType.String or FieldType.Multiline or FieldType.Path;
}

/// <summary>Shorthand factory methods for <see cref="FieldDescriptor"/>s.</summary>
public static class Fields
{
    /// <summary>A single-line text field.</summary>
    public static FieldDescriptor Text(string name, string description, bool required = false, string? defaultValue = null, string? example = null) =>
        new(name, FieldType.String, description) { Required = required, Default = defaultValue, Example = example };

    /// <summary>A multi-line text field.</summary>
    public static FieldDescriptor Multiline(string name, string description, bool required = false, string? example = null) =>
        new(name, FieldType.Multiline, description) { Required = required, Example = example };

    /// <summary>A file or folder path.</summary>
    public static FieldDescriptor Path(string name, string description, bool required = false, string? example = null) =>
        new(name, FieldType.Path, description) { Required = required, Example = example };

    /// <summary>A whole number.</summary>
    public static FieldDescriptor Integer(string name, string description, bool required = false, long? defaultValue = null, double? min = null, double? max = null, string? example = null) =>
        new(name, FieldType.Integer, description) { Required = required, Default = defaultValue, Minimum = min, Maximum = max, Example = example };

    /// <summary>A number.</summary>
    public static FieldDescriptor Number(string name, string description, bool required = false, double? defaultValue = null, double? min = null, double? max = null) =>
        new(name, FieldType.Number, description) { Required = required, Default = defaultValue, Minimum = min, Maximum = max };

    /// <summary>A true/false switch.</summary>
    public static FieldDescriptor Boolean(string name, string description, bool? defaultValue = null) =>
        new(name, FieldType.Boolean, description) { Default = defaultValue };

    /// <summary>One of a fixed set of values.</summary>
    public static FieldDescriptor Choice(string name, string description, IReadOnlyList<string> values, bool required = false, string? defaultValue = null) =>
        new(name, FieldType.Enum, description) { Required = required, Default = defaultValue, AllowedValues = values };

    /// <summary>A duration.</summary>
    public static FieldDescriptor Duration(string name, string description, bool required = false, string? defaultValue = null, string? example = null) =>
        new(name, FieldType.Duration, description) { Required = required, Default = defaultValue, Example = example };

    /// <summary>A time of day.</summary>
    public static FieldDescriptor Time(string name, string description, bool required = false, string? example = null) =>
        new(name, FieldType.Time, description) { Required = required, Example = example };

    /// <summary>A list of values, optionally restricted to <paramref name="values"/>.</summary>
    public static FieldDescriptor List(string name, string description, IReadOnlyList<string>? values = null, bool required = false, string? example = null) =>
        new(name, FieldType.StringList, description) { Required = required, AllowedValues = values, Example = example };

    /// <summary>One or more app patterns.</summary>
    public static FieldDescriptor Apps(string name, string description, bool required = false, string? example = null) =>
        new(name, FieldType.AppList, description) { Required = required, Example = example };

    /// <summary>One or more users.</summary>
    public static FieldDescriptor Users(string name, string description, bool required = false, string? example = null) =>
        new(name, FieldType.UserList, description) { Required = required, Example = example };

    /// <summary>A nested list of conditions.</summary>
    public static FieldDescriptor Conditions(string name, string description) =>
        new(name, FieldType.ConditionList, description) { Required = true };
}
