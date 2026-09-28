namespace AutoSettings.Core.Catalog;

/// <summary>Helpers for <see cref="ComponentDescriptor.Validate"/>.</summary>
public static class ValidationRules
{
    /// <summary>Returns <paramref name="message"/> when <paramref name="ok"/> is false.</summary>
    public static IEnumerable<string> Require(bool ok, string message)
    {
        if (!ok)
            yield return message;
    }
}
