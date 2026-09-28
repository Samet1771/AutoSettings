using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AutoSettings.Core.Events;

namespace AutoSettings.Core.Engine;

/// <summary>Case-insensitive wildcard matching: <c>*</c> matches any text, <c>?</c> one character.</summary>
public static class Wildcard
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="text"/> matches <paramref name="pattern"/>.</summary>
    public static bool IsMatch(string pattern, string? text)
    {
        if (text is null)
            return false;
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return string.Equals(pattern, text, StringComparison.OrdinalIgnoreCase);
        var regex = Cache.GetOrAdd(pattern, p => new Regex(
            "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));
        return regex.IsMatch(text);
    }
}

/// <summary>Matches app patterns against processes.</summary>
public static class AppPattern
{
    /// <summary>
    /// Whether <paramref name="process"/> matches <paramref name="pattern"/>. Patterns containing a
    /// backslash are matched against the full path; others against the exe name, with or without <c>.exe</c>.
    /// </summary>
    public static bool Matches(string pattern, ProcessInfo? process)
    {
        if (process is null)
            return false;
        pattern = pattern.Trim().Trim('"');
        if (pattern.Length == 0)
            return false;
        if (pattern == "*")
            return true;

        if (pattern.Contains('\\') || pattern.Contains('/'))
            return Wildcard.IsMatch(pattern.Replace('/', '\\'), process.Path);

        return Wildcard.IsMatch(pattern, process.Name)
            || Wildcard.IsMatch(pattern, Path.GetFileNameWithoutExtension(process.Name));
    }

    /// <summary>Whether any of <paramref name="patterns"/> matches.</summary>
    public static bool MatchesAny(IEnumerable<string> patterns, ProcessInfo? process) =>
        patterns.Any(p => Matches(p, process));
}

/// <summary>Matches user patterns against users.</summary>
public static class UserPattern
{
    /// <summary>Whether <paramref name="user"/> matches <paramref name="pattern"/> (name, DOMAIN\name or SID, wildcards allowed).</summary>
    public static bool Matches(string pattern, UserInfo? user)
    {
        if (user is null)
            return false;
        pattern = pattern.Trim();
        if (pattern.StartsWith(@".\", StringComparison.Ordinal))
            pattern = pattern[2..];
        return Wildcard.IsMatch(pattern, user.Name)
            || Wildcard.IsMatch(pattern, user.QualifiedName)
            || (user.Sid is not null && Wildcard.IsMatch(pattern, user.Sid));
    }

    /// <summary>Whether any of <paramref name="patterns"/> matches.</summary>
    public static bool MatchesAny(IEnumerable<string> patterns, UserInfo? user) =>
        patterns.Any(p => Matches(p, user));
}
