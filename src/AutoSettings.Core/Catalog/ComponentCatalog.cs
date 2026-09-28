using AutoSettings.Core.Model;

namespace AutoSettings.Core.Catalog;

/// <summary>The set of known trigger, condition and action types.</summary>
public sealed class ComponentCatalog
{
    /// <summary>Field added to every action: keep going when the action fails.</summary>
    public static readonly FieldDescriptor ContinueOnError = Fields.Boolean(
        "continue_on_error",
        "Keep running the next actions even if this one fails.",
        defaultValue: false);

    private readonly Dictionary<(ComponentKind, string), ComponentDescriptor> _byType = new();

    /// <summary>Creates a catalog. Every action automatically gets the <c>continue_on_error</c> field.</summary>
    public ComponentCatalog(IEnumerable<ComponentDescriptor> descriptors)
    {
        var all = new List<ComponentDescriptor>();
        foreach (var descriptor in descriptors)
        {
            var d = descriptor;
            if (d.Kind == ComponentKind.Action && d.Field(ContinueOnError.Name) is null)
                d = d with { Fields = [.. d.Fields, ContinueOnError] };
            if (!_byType.TryAdd((d.Kind, d.Type), d))
                throw new ArgumentException($"Duplicate {d.Kind} type '{d.Type}'.", nameof(descriptors));
            all.Add(d);
        }
        All = all;
    }

    /// <summary>The built-in catalog.</summary>
    public static ComponentCatalog Default { get; } = new(BuiltInComponents.All);

    /// <summary>All descriptors in registration order.</summary>
    public IReadOnlyList<ComponentDescriptor> All { get; }

    /// <summary>Descriptors of one kind.</summary>
    public IEnumerable<ComponentDescriptor> OfKind(ComponentKind kind) => All.Where(d => d.Kind == kind);

    /// <summary>Finds a descriptor, or <c>null</c>.</summary>
    public ComponentDescriptor? Find(ComponentKind kind, string type) =>
        _byType.TryGetValue((kind, type), out var d) ? d : null;

    /// <summary>
    /// Returns a copy of <paramref name="config"/> where missing fields are filled from their defaults
    /// and values are converted to their typed form. Invalid values are left as they are.
    /// </summary>
    public ComponentConfig WithDefaults(ComponentKind kind, ComponentConfig config)
    {
        var descriptor = Find(kind, config.Type);
        if (descriptor is null)
            return config;
        var copy = config.Clone();
        foreach (var field in descriptor.Fields)
        {
            var raw = copy.Parameters.TryGetValue(field.Name, out var v) && v is not null ? v : field.Default;
            if (raw is null)
                continue;
            copy.Parameters[field.Name] = ValueConverter.TryConvert(raw, field, out var typed, out _) ? typed : raw;
        }
        return copy;
    }
}
