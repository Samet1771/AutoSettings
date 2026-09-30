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

    /// <summary>The catalog of built-in components only (no plugins).</summary>
    public static ComponentCatalog BuiltIn { get; } = new(BuiltInComponents.All);

    /// <summary>
    /// Builds a catalog from the built-in components and the components of plugins. A plugin is added
    /// whole or not at all: when one of its types is already taken (by a built-in component or by a plugin
    /// added before it), or appears twice in the plugin, the plugin is left out and listed in
    /// <see cref="CatalogComposition.Rejected"/>. The built-in components are always kept.
    /// </summary>
    public static CatalogComposition Compose(IEnumerable<ComponentDescriptor> builtIn, IEnumerable<PluginContribution> plugins)
    {
        var accepted = builtIn.ToList();
        var taken = accepted.Select(d => (d.Kind, d.Type)).ToHashSet(KindTypeComparer.Instance);
        var rejected = new List<PluginConflict>();
        foreach (var plugin in plugins)
        {
            var own = new HashSet<(ComponentKind, string)>(KindTypeComparer.Instance);
            var clash = plugin.Components.FirstOrDefault(d => taken.Contains((d.Kind, d.Type)) || !own.Add((d.Kind, d.Type)));
            if (clash is not null)
            {
                rejected.Add(new PluginConflict(plugin.Source.PluginId ?? "", $"{clash.Kind} type '{clash.Type}' already exists."));
                continue;
            }
            foreach (var d in plugin.Components)
            {
                accepted.Add(d with { Source = plugin.Source });
                taken.Add((d.Kind, d.Type));
            }
        }
        return new CatalogComposition(new ComponentCatalog(accepted), rejected);
    }

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

    /// <summary>Compares (kind, type) pairs ignoring case, like handler lookup does, so plugins cannot shadow a type by case.</summary>
    private sealed class KindTypeComparer : IEqualityComparer<(ComponentKind Kind, string Type)>
    {
        public static readonly KindTypeComparer Instance = new();

        public bool Equals((ComponentKind Kind, string Type) x, (ComponentKind Kind, string Type) y) =>
            x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.Type, y.Type);

        public int GetHashCode((ComponentKind Kind, string Type) obj) =>
            HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Type));
    }
}

/// <summary>The components one plugin adds to the catalog.</summary>
/// <param name="Source">The plugin (its <see cref="ComponentSource.PluginId"/> must be set).</param>
/// <param name="Components">Its triggers, conditions and actions.</param>
public sealed record PluginContribution(ComponentSource Source, IReadOnlyList<ComponentDescriptor> Components);

/// <summary>A plugin that was left out of the catalog.</summary>
/// <param name="PluginId">The plugin.</param>
/// <param name="Reason">Why, in plain language.</param>
public sealed record PluginConflict(string PluginId, string Reason);

/// <summary>The result of <see cref="ComponentCatalog.Compose"/>.</summary>
/// <param name="Catalog">The catalog with the built-in components and every accepted plugin.</param>
/// <param name="Rejected">Plugins that were left out.</param>
public sealed record CatalogComposition(ComponentCatalog Catalog, IReadOnlyList<PluginConflict> Rejected);
