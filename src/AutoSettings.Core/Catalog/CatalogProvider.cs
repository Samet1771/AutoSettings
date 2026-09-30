namespace AutoSettings.Core.Catalog;

/// <summary>Gives the current component catalog, which changes when plugins are installed, removed or turned on or off.</summary>
public interface IComponentCatalogProvider
{
    /// <summary>The catalog to use now.</summary>
    ComponentCatalog Current { get; }

    /// <summary>Raised after <see cref="Current"/> changed. May be raised on any thread.</summary>
    event EventHandler? Changed;
}

/// <summary>A catalog provider whose catalog is replaced with <see cref="Update"/>.</summary>
public sealed class ComponentCatalogProvider : IComponentCatalogProvider
{
    private volatile ComponentCatalog _current;

    /// <summary>Creates a provider that starts with <paramref name="initial"/> (the built-in catalog by default).</summary>
    public ComponentCatalogProvider(ComponentCatalog? initial = null) => _current = initial ?? ComponentCatalog.BuiltIn;

    /// <inheritdoc />
    public ComponentCatalog Current => _current;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Replaces the catalog and raises <see cref="Changed"/>.</summary>
    public void Update(ComponentCatalog catalog)
    {
        _current = catalog;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
