using AutoSettings.Core.Model;

namespace AutoSettings.Core.Catalog;

/// <summary>Short plain-language descriptions of components, for lists in the UI.</summary>
public static class ComponentSummary
{
    /// <summary>Describes a component, e.g. "App focused: POWERPNT.EXE" or "Set volume: 30".</summary>
    public static string Describe(ComponentKind kind, ComponentConfig component, ComponentCatalog? catalog = null)
    {
        var descriptor = (catalog ?? ComponentCatalog.BuiltIn).Find(kind, component.Type);
        if (descriptor is null)
            return component.Type;

        var nested = component.GetComponents("conditions");
        if (nested.Count > 0)
            return $"{descriptor.Title} ({string.Join("; ", nested.Select(n => Describe(ComponentKind.Condition, n, catalog)))})";

        var values = descriptor.Fields
            .Where(f => f.Name != ComponentCatalog.ContinueOnError.Name && f.Type != FieldType.ConditionList && component.Has(f.Name))
            .Select(f => Shorten(ValueConverter.ToDisplay(component.Parameters[f.Name])))
            .ToList();
        return values.Count == 0 ? descriptor.Title : $"{descriptor.Title}: {string.Join(", ", values)}";
    }

    /// <summary>Describes a list of components joined with <paramref name="separator"/>.</summary>
    public static string DescribeAll(ComponentKind kind, IEnumerable<ComponentConfig> components, string separator, ComponentCatalog? catalog = null) =>
        string.Join(separator, components.Select(c => Describe(kind, c, catalog)));

    private static string Shorten(string text)
    {
        text = text.Replace("\r", "").Replace('\n', ' ').Trim();
        return text.Length <= 60 ? text : text[..57] + "...";
    }
}
