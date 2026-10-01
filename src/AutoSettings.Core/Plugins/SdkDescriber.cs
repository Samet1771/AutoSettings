using System.Reflection;
using AutoSettings.Core.Catalog;
using AutoSettings.Sdk;

namespace AutoSettings.Core.Plugins;

/// <summary>
/// Reads the <see cref="PluginComponentAttribute"/>, <see cref="FieldAttribute"/> and <see cref="LocalizedAttribute"/>
/// attributes of a .NET plugin and turns them into manifest components. The packing tool writes them into
/// <c>plugin.yaml</c>, so AutoSettings knows a .NET plugin's components without running it.
/// </summary>
public static class SdkDescriber
{
    /// <summary>The components declared by the classes of <paramref name="assembly"/>.</summary>
    /// <exception cref="InvalidOperationException">A class has component attributes but does not implement a component interface.</exception>
    public static List<ManifestComponent> Describe(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>().ToArray();
        }
        return Describe(types.Where(t => t.GetCustomAttributes<PluginComponentAttribute>(inherit: false).Any()).OrderBy(t => t.FullName, StringComparer.Ordinal));
    }

    /// <summary>The components declared by <paramref name="types"/>.</summary>
    public static List<ManifestComponent> Describe(IEnumerable<Type> types)
    {
        var result = new List<ManifestComponent>();
        foreach (var type in types)
            result.AddRange(Describe(type));
        return result;
    }

    /// <summary>The components declared by one class (a trigger class may declare several events).</summary>
    public static List<ManifestComponent> Describe(Type type)
    {
        var kind = KindOf(type)
            ?? throw new InvalidOperationException($"{type.FullName} has [PluginComponent] but implements none of IPluginAction, IPluginCondition or IPluginTrigger.");
        var declarations = type.GetCustomAttributes<PluginComponentAttribute>(inherit: false).ToList();
        var fields = type.GetCustomAttributes<FieldAttribute>(inherit: false).Select(ToField).ToList();
        var translations = type.GetCustomAttributes<LocalizedAttribute>(inherit: false).ToList();

        var result = new List<ManifestComponent>();
        foreach (var declaration in declarations)
        {
            var mine = translations.Where(t => t.Type is null ? declarations.Count == 1 : string.Equals(t.Type, declaration.Type, StringComparison.Ordinal)).ToList();
            result.Add(new ManifestComponent
            {
                Type = declaration.Type,
                Kind = kind,
                Title = new LocalizedString(declaration.Title, mine.ToDictionary(t => t.Language.ToLowerInvariant(), t => t.Title)),
                Description = new LocalizedString(declaration.Description,
                    mine.Where(t => t.Description is not null).ToDictionary(t => t.Language.ToLowerInvariant(), t => t.Description!)),
                Category = declaration.Category,
                RunsAs = declaration.RunsAs == PluginScope.Machine ? ExecutionScope.Machine : ExecutionScope.User,
                Revertible = kind == ComponentKind.Action && typeof(IRevertiblePluginAction).IsAssignableFrom(type),
                AvailableIn = (ScopeSupport)(int)declaration.AvailableIn,
                Opposite = declaration.OppositeEvent,
                Example = declaration.Example,
                Notes = declaration.Notes,
                Fields = fields.Select(Copy).ToList(),
            });
        }
        return result;
    }

    /// <summary>Whether <paramref name="type"/> is an action, condition or trigger, or <c>null</c>.</summary>
    public static ComponentKind? KindOf(Type type) =>
        typeof(IPluginAction).IsAssignableFrom(type) ? ComponentKind.Action
        : typeof(IPluginCondition).IsAssignableFrom(type) ? ComponentKind.Condition
        : typeof(IPluginTrigger).IsAssignableFrom(type) ? ComponentKind.Trigger
        : null;

    /// <summary>The catalog field type for an SDK field kind (the names match).</summary>
    public static FieldType ToFieldType(FieldKind kind) => Enum.Parse<FieldType>(kind.ToString());

    private static ManifestField ToField(FieldAttribute attribute) => new()
    {
        Name = attribute.Name,
        Type = ToFieldType(attribute.Kind),
        Description = LocalizedString.Of(attribute.Description),
        Required = attribute.Required,
        Default = attribute.Default,
        Values = attribute.Values?.ToList(),
        Minimum = double.IsNaN(attribute.Minimum) ? null : attribute.Minimum,
        Maximum = double.IsNaN(attribute.Maximum) ? null : attribute.Maximum,
        Example = attribute.Example,
        Key = attribute.KeyField,
        Placeholders = attribute.Placeholders,
    };

    private static ManifestField Copy(ManifestField f) => new()
    {
        Name = f.Name, Type = f.Type, Description = f.Description, Required = f.Required, Default = f.Default,
        Values = f.Values?.ToList(), Minimum = f.Minimum, Maximum = f.Maximum, Example = f.Example, Key = f.Key, Placeholders = f.Placeholders,
    };
}
