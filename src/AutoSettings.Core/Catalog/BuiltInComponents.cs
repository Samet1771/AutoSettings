namespace AutoSettings.Core.Catalog;

/// <summary>Every built-in trigger, condition and action.</summary>
public static class BuiltInComponents
{
    /// <summary>All built-in descriptors.</summary>
    public static IEnumerable<ComponentDescriptor> All =>
        BuiltInTriggers.All.Concat(BuiltInConditions.All).Concat(BuiltInActions.All);
}
