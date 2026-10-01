using System.Reflection;

namespace AutoSettings.Core.Updates;

/// <summary>The version of the running AutoSettings.</summary>
public static class AppVersion
{
    /// <summary>The version of the running program, for example <c>0.3.0-beta.2</c> (<c>0.0.0</c> when unknown).</summary>
    public static SemVersion Current { get; } = Read();

    private static SemVersion Read()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return SemVersion.TryParse(informational)
            ?? SemVersion.TryParse(assembly.GetName().Version?.ToString(3))
            ?? SemVersion.Parse("0.0.0");
    }
}
