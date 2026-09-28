using System.Globalization;

namespace AutoSettings.Core.Updates;

/// <summary>
/// A semantic version such as <c>0.3.0</c> or <c>0.3.0-beta.2</c>, as used in release tags (<c>v0.3.0</c>).
/// Compared by SemVer precedence: a prerelease comes before its release; build metadata (<c>+sha</c>) is ignored.
/// </summary>
public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    private SemVersion(int major, int minor, int patch, string prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    /// <summary>Major version.</summary>
    public int Major { get; }

    /// <summary>Minor version.</summary>
    public int Minor { get; }

    /// <summary>Patch version.</summary>
    public int Patch { get; }

    /// <summary>Prerelease label without the dash, for example <c>beta.2</c>; empty for a release.</summary>
    public string Prerelease { get; }

    /// <summary>True for versions like <c>0.3.0-beta.1</c>.</summary>
    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>Parses <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2</c>, <c>1.2.3-beta.1</c> and <c>1.2.3+build</c>. Returns null if it is not a version.</summary>
    public static SemVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        var plus = value.IndexOf('+');
        if (plus >= 0)
            value = value[..plus];

        var prerelease = "";
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = value[(dash + 1)..];
            value = value[..dash];
            if (prerelease.Length == 0 || prerelease.Split('.').Any(p => p.Length == 0))
                return null;
        }

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 3)
            return null;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        }
        return new SemVersion(numbers[0], numbers[1], numbers[2], prerelease);
    }

    /// <summary>Parses a version or throws <see cref="FormatException"/>.</summary>
    public static SemVersion Parse(string text) =>
        TryParse(text) ?? throw new FormatException($"'{text}' is not a version like 1.2.3.");

    /// <summary>The version Windows Installer understands: <c>major.minor.patch</c> without the prerelease label.</summary>
    public string ToMsiVersion() => $"{Major}.{Minor}.{Patch}";

    /// <inheritdoc />
    public int CompareTo(SemVersion? other)
    {
        if (other is null)
            return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0)
            return result;

        if (!IsPrerelease || !other.IsPrerelease)
            return other.IsPrerelease.CompareTo(IsPrerelease);

        var mine = Prerelease.Split('.');
        var theirs = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var a = mine[i];
            var b = theirs[i];
            var aNumeric = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bNumeric = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);
            result = (aNumeric, bNumeric) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            };
            if (result != 0)
                return result;
        }
        return mine.Length.CompareTo(theirs.Length);
    }

    /// <inheritdoc />
    public bool Equals(SemVersion? other) => CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SemVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    /// <inheritdoc />
    public override string ToString() => IsPrerelease ? $"{ToMsiVersion()}-{Prerelease}" : ToMsiVersion();

    /// <summary>Greater than.</summary>
    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;

    /// <summary>Less than.</summary>
    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;
}
