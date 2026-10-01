using System.Reflection;
using System.Runtime.Loader;

namespace AutoSettings.Core.Plugins;

/// <summary>
/// Loads a .NET plugin and its dependencies from the plugin's folder, apart from the app's own assemblies. Only
/// <c>AutoSettings.Sdk</c> is shared, so the plugin and AutoSettings agree on the SDK types; every other dependency
/// (even one AutoSettings also uses) comes from the plugin's folder in the version the plugin was built with.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>Creates a context for the plugin assembly at <paramref name="mainAssemblyPath"/>.</summary>
    public PluginLoadContext(string mainAssemblyPath)
        : base($"Plugin {Path.GetFileNameWithoutExtension(mainAssemblyPath)}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    /// <summary>Name of the shared SDK assembly.</summary>
    public const string SdkAssemblyName = "AutoSettings.Sdk";

    /// <summary>Loads the plugin assembly.</summary>
    public static Assembly LoadPlugin(string mainAssemblyPath) =>
        new PluginLoadContext(mainAssemblyPath).LoadFromAssemblyPath(Path.GetFullPath(mainAssemblyPath));

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, SdkAssemblyName, StringComparison.OrdinalIgnoreCase))
            return null; // The default context's SDK.
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc />
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
