using AutoSettings.Core.Catalog;

namespace AutoSettings.Core.Config;

/// <summary>Reads and validates configuration text in one step.</summary>
public static class ConfigLoader
{
    /// <summary>Parses and validates <paramref name="yaml"/>.</summary>
    /// <param name="yaml">The file contents.</param>
    /// <param name="scope">Whether this is a personal or a machine automation file.</param>
    /// <param name="catalog">Component catalog; the built-in one by default.</param>
    public static ConfigLoadResult Load(string yaml, ExecutionScope scope, ComponentCatalog? catalog = null)
    {
        var (config, issues) = YamlConfigReader.Read(yaml);
        issues.AddRange(new ConfigValidator(catalog).Validate(config, scope));
        var ordered = issues
            .OrderBy(i => i.Location?.Line ?? int.MaxValue)
            .ThenBy(i => i.Location?.Column ?? int.MaxValue)
            .ToList();
        return new ConfigLoadResult(config, ordered);
    }
}
