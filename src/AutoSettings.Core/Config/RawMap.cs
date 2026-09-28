using AutoSettings.Core.Model;

namespace AutoSettings.Core.Config;

/// <summary>A YAML mapping read from a file, remembering where it and each key came from.</summary>
public sealed class RawMap : Dictionary<string, object?>
{
    /// <summary>Creates an empty map.</summary>
    public RawMap(SourceLocation? location = null) : base(StringComparer.Ordinal) => Location = location;

    /// <summary>Where the mapping starts.</summary>
    public SourceLocation? Location { get; }

    /// <summary>Where each key is.</summary>
    public Dictionary<string, SourceLocation> KeyLocations { get; } = new(StringComparer.Ordinal);

    /// <summary>Where <paramref name="key"/> is, falling back to the map's location.</summary>
    public SourceLocation? LocationOf(string key) =>
        KeyLocations.TryGetValue(key, out var location) ? location : Location;
}
