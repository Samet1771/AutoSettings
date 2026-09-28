using AutoSettings.Core.Catalog;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Editing;

/// <summary>What <see cref="ConfigMerge.Merge"/> added.</summary>
/// <param name="AutomationIds">Ids of the added automations (after renaming).</param>
/// <param name="ProfileIds">Ids of the added profiles (after renaming).</param>
public sealed record MergeResult(IReadOnlyList<string> AutomationIds, IReadOnlyList<string> ProfileIds);

/// <summary>Combines configurations (templates, import) and extracts parts of one (export).</summary>
public static class ConfigMerge
{
    /// <summary>
    /// Adds copies of everything in <paramref name="source"/> to <paramref name="target"/>. Ids that already
    /// exist are renamed (<c>gaming</c> → <c>gaming-2</c>) and references to renamed profiles are updated.
    /// </summary>
    public static MergeResult Merge(AutomationConfig target, AutomationConfig source)
    {
        var profileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var addedProfiles = new List<string>();
        foreach (var profile in source.Profiles)
        {
            var copy = ConfigCloner.Clone(profile);
            copy.Id = ConfigDocument.Unique(copy.Id, target.Profiles.Select(p => p.Id));
            profileIds[profile.Id] = copy.Id;
            target.Profiles.Add(copy);
            addedProfiles.Add(copy.Id);
        }

        var addedAutomations = new List<string>();
        foreach (var automation in source.Automations)
        {
            var copy = ConfigCloner.Clone(automation);
            copy.Id = ConfigDocument.Unique(copy.Id, target.Automations.Select(a => a.Id));
            foreach (var component in copy.Conditions.Concat(copy.Actions))
                RenameProfileReferences(component, profileIds);
            target.Automations.Add(copy);
            addedAutomations.Add(copy.Id);
        }
        return new MergeResult(addedAutomations, addedProfiles);
    }

    /// <summary>
    /// Returns a new configuration with the chosen automations and profiles, plus every profile the chosen
    /// automations use, so the result is valid on its own.
    /// </summary>
    public static AutomationConfig Export(AutomationConfig config, IEnumerable<string> automationIds, IEnumerable<string> profileIds)
    {
        var automationSet = new HashSet<string>(automationIds, StringComparer.OrdinalIgnoreCase);
        var profileSet = new HashSet<string>(profileIds, StringComparer.OrdinalIgnoreCase);

        var automations = config.Automations.Where(a => automationSet.Contains(a.Id)).ToList();
        foreach (var automation in automations)
        {
            foreach (var component in automation.Conditions.Concat(automation.Actions))
                profileSet.UnionWith(ProfileReferences(component));
        }

        return new AutomationConfig
        {
            Automations = automations.Select(ConfigCloner.Clone).ToList(),
            Profiles = config.Profiles.Where(p => profileSet.Contains(p.Id)).Select(ConfigCloner.Clone).ToList(),
        };
    }

    /// <summary>Profile ids referenced by a component (profile.apply/revert, profile_active, nested conditions).</summary>
    public static IEnumerable<string> ProfileReferences(ComponentConfig component)
    {
        if (IsProfileReference(component) && component.GetString("profile") is { Length: > 0 } id)
            yield return id;
        foreach (var nested in component.GetComponents("conditions"))
        {
            foreach (var reference in ProfileReferences(nested))
                yield return reference;
        }
    }

    private static void RenameProfileReferences(ComponentConfig component, IReadOnlyDictionary<string, string> renames)
    {
        if (IsProfileReference(component) && component.GetString("profile") is { } id && renames.TryGetValue(id, out var renamed))
            component.Parameters["profile"] = renamed;
        foreach (var nested in component.GetComponents("conditions"))
            RenameProfileReferences(nested, renames);
    }

    private static bool IsProfileReference(ComponentConfig component) =>
        component.Type is BuiltInActions.ProfileApply or BuiltInActions.ProfileRevert or "profile_active";
}
