using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Editing;

/// <summary>Converts between field values and the text shown in form editors.</summary>
public static class FieldText
{
    private static readonly char[] ListSeparators = [',', '\n', ';'];

    /// <summary>Whether the field is edited as a list of values.</summary>
    public static bool IsList(FieldDescriptor field) =>
        field.Type is FieldType.StringList or FieldType.AppList or FieldType.UserList;

    /// <summary>Text for a single-value field ("" when not set).</summary>
    public static string Format(object? value) => value switch
    {
        null => "",
        IEnumerable<string> list => string.Join(", ", list),
        _ => ValueConverter.ToText(value) ?? "",
    };

    /// <summary>Items for a list field.</summary>
    public static List<string> FormatList(object? value) =>
        ValueConverter.TryToStringList(value, out var list) ? list : [];

    /// <summary>
    /// The raw value for a field from editor text: <c>null</c> when empty, a list for list fields,
    /// otherwise the trimmed text (the validator converts it to the field's type).
    /// </summary>
    public static object? Parse(FieldDescriptor field, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (IsList(field))
        {
            var items = text.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return items.Count == 0 ? null : items;
        }
        return field.Type == FieldType.Multiline ? text.Replace("\r\n", "\n") : text.Trim();
    }

    /// <summary>The raw value for a list field from its items (null when empty).</summary>
    public static object? ParseList(IEnumerable<string> items)
    {
        var list = items.Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
        return list.Count == 0 ? null : list;
    }

    /// <summary>Validates one component on its own and returns its error messages.</summary>
    public static IReadOnlyList<string> Validate(ComponentKind kind, ComponentConfig component, ExecutionScope scope, ComponentCatalog? catalog = null) =>
        new ConfigValidator(catalog).NormalizeComponent(kind, ConfigCloner.Clone(component), scope)
            .Where(i => i.Severity == IssueSeverity.Error)
            .Select(i => StripLabel(i.Message, component.Type))
            .ToList();

    private static string StripLabel(string message, string type)
    {
        var prefix = $"{type} ({type}): ";
        return message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message;
    }
}
