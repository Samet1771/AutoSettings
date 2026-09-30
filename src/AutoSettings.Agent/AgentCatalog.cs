using AutoSettings.Core.Catalog;

namespace AutoSettings.Agent;

/// <summary>
/// The components this agent knows: the built-in ones and, later, those of the plugins available to this user.
/// Windows, the editor and the engine all read it from here so they agree after plugins change.
/// </summary>
public static class AgentCatalog
{
    /// <summary>Replace the catalog through this provider.</summary>
    public static ComponentCatalogProvider Provider { get; } = new();

    /// <summary>The catalog to use now.</summary>
    public static ComponentCatalog Current => Provider.Current;
}
