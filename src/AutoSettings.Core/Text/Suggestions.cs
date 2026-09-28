namespace AutoSettings.Core.Text;

/// <summary>"Did you mean ...?" suggestions for misspelled names.</summary>
public static class Suggestions
{
    /// <summary>Returns the candidate closest to <paramref name="input"/>, or <c>null</c> if none is close enough.</summary>
    public static string? Closest(string input, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = Distance(input.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        var threshold = Math.Max(2, input.Length / 3);
        return bestDistance <= threshold ? best : null;
    }

    /// <summary>Levenshtein edit distance.</summary>
    public static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    /// <summary>Formats a " Did you mean 'x'?" suffix, or an empty string.</summary>
    public static string DidYouMean(string input, IEnumerable<string> candidates) =>
        Closest(input, candidates) is { } match ? $" Did you mean '{match}'?" : "";
}
