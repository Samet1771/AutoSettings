namespace AutoSettings.Core.Model;

/// <summary>
/// One trigger, condition or action entry: a <see cref="Type"/> (for example <c>app_focused</c>
/// or <c>audio.volume</c>) plus its parameters.
/// </summary>
/// <remarks>
/// Parameter values are either raw values as read from YAML (strings, lists, maps) or, after
/// validation, normalized values: <see cref="string"/>, <see cref="long"/>, <see cref="double"/>,
/// <see cref="bool"/>, <see cref="TimeSpan"/>, <see cref="TimeOnly"/>, <c>List&lt;string&gt;</c>
/// or <c>List&lt;ComponentConfig&gt;</c>. The typed getters accept both forms.
/// </remarks>
public sealed class ComponentConfig
{
    /// <summary>Creates a component.</summary>
    public ComponentConfig(string type, IEnumerable<KeyValuePair<string, object?>>? parameters = null, SourceLocation? location = null)
    {
        Type = type;
        Parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (parameters is not null)
        {
            foreach (var (key, value) in parameters)
                Parameters[key] = value;
        }
        Location = location;
    }

    /// <summary>The component type, for example <c>app_started</c>.</summary>
    public string Type { get; }

    /// <summary>Parameters keyed by field name (snake_case, as written in YAML).</summary>
    public Dictionary<string, object?> Parameters { get; }

    /// <summary>Where this entry starts in its YAML file, if loaded from one.</summary>
    public SourceLocation? Location { get; }

    /// <summary>Whether a non-null value is set for <paramref name="name"/>.</summary>
    public bool Has(string name) => Parameters.TryGetValue(name, out var v) && v is not null;

    /// <summary>Gets a parameter as text, or <c>null</c> when missing.</summary>
    public string? GetString(string name) =>
        Parameters.TryGetValue(name, out var v) ? ValueConverter.ToText(v) : null;

    /// <summary>Gets an integer parameter, or <c>null</c> when missing or not an integer.</summary>
    public long? GetInteger(string name) =>
        Parameters.TryGetValue(name, out var v) && ValueConverter.TryToInteger(v, out var r) ? r : null;

    /// <summary>Gets a number parameter, or <c>null</c> when missing or not a number.</summary>
    public double? GetNumber(string name) =>
        Parameters.TryGetValue(name, out var v) && ValueConverter.TryToNumber(v, out var r) ? r : null;

    /// <summary>Gets a boolean parameter, or <c>null</c> when missing or not a boolean.</summary>
    public bool? GetBoolean(string name) =>
        Parameters.TryGetValue(name, out var v) && ValueConverter.TryToBoolean(v, out var r) ? r : null;

    /// <summary>Gets a duration parameter, or <c>null</c> when missing or invalid.</summary>
    public TimeSpan? GetDuration(string name) =>
        Parameters.TryGetValue(name, out var v) && ValueConverter.TryToDuration(v, out var r) ? r : null;

    /// <summary>Gets a time-of-day parameter, or <c>null</c> when missing or invalid.</summary>
    public TimeOnly? GetTime(string name) =>
        Parameters.TryGetValue(name, out var v) && ValueConverter.TryToTime(v, out var r) ? r : null;

    /// <summary>Gets a list parameter. A single value is returned as a one-item list.</summary>
    public IReadOnlyList<string> GetStringList(string name) =>
        Parameters.TryGetValue(name, out var v) && ValueConverter.TryToStringList(v, out var r) ? r : [];

    /// <summary>Gets a list of nested components (used by <c>and</c>/<c>or</c>/<c>not</c> conditions).</summary>
    public IReadOnlyList<ComponentConfig> GetComponents(string name) =>
        Parameters.TryGetValue(name, out var v) && v is IEnumerable<ComponentConfig> list ? list.ToList() : [];

    /// <summary>Returns a shallow copy.</summary>
    public ComponentConfig Clone() => new(Type, Parameters, Location);

    /// <summary>Returns a copy with <paramref name="name"/> set to <paramref name="value"/>.</summary>
    public ComponentConfig With(string name, object? value)
    {
        var copy = Clone();
        copy.Parameters[name] = value;
        return copy;
    }

    /// <inheritdoc />
    public override string ToString() =>
        Parameters.Count == 0
            ? Type
            : $"{Type} ({string.Join(", ", Parameters.Where(p => p.Value is not null).Select(p => $"{p.Key}: {ValueConverter.ToDisplay(p.Value)}"))})";
}
