using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Editing;

/// <summary>
/// An automation file being edited: add, duplicate, remove, reorder and toggle automations and
/// profiles, edit single items as YAML, and write the result back.
/// </summary>
public sealed class ConfigDocument
{
    /// <summary>Header written at the top of files saved by the editor.</summary>
    public const string Header = """
        AutoSettings automations. Edited with the AutoSettings editor.
        Reference: https://github.com/Samet1771/AutoSettings/tree/main/docs/reference
        """;

    private readonly ComponentCatalog _catalog;

    /// <summary>Starts editing a copy of <paramref name="config"/>.</summary>
    public ConfigDocument(AutomationConfig config, ExecutionScope scope, ComponentCatalog? catalog = null)
    {
        Config = ConfigCloner.Clone(config);
        Scope = scope;
        _catalog = catalog ?? ComponentCatalog.BuiltIn;
    }

    /// <summary>The configuration being edited.</summary>
    public AutomationConfig Config { get; }

    /// <summary>Personal or machine file.</summary>
    public ExecutionScope Scope { get; }

    /// <summary>Validates the whole document (normalizing values).</summary>
    public IReadOnlyList<ConfigIssue> Validate() => new ConfigValidator(_catalog).Validate(Config, Scope);

    /// <summary>Serializes the document. Comments of the original file are not kept.</summary>
    public string ToYaml() => YamlConfigWriter.Write(Config, Header);

    /// <summary>Adds a new, empty automation and returns it.</summary>
    public Automation AddAutomation(string name)
    {
        var automation = new Automation { Id = UniqueAutomationId(ConfigValidator.Slug(name)), Name = name };
        Config.Automations.Add(automation);
        return automation;
    }

    /// <summary>Adds a new, empty profile and returns it.</summary>
    public Profile AddProfile(string name)
    {
        var profile = new Profile { Id = UniqueProfileId(ConfigValidator.Slug(name)), Name = name };
        Config.Profiles.Add(profile);
        return profile;
    }

    /// <summary>Inserts a copy of an automation right after it. Returns the copy, or null if not found.</summary>
    public Automation? DuplicateAutomation(string id)
    {
        var index = IndexOfAutomation(id);
        if (index < 0)
            return null;
        var copy = ConfigCloner.Clone(Config.Automations[index]);
        copy.Id = UniqueAutomationId(copy.Id);
        copy.Name = string.IsNullOrWhiteSpace(copy.Name) ? null : copy.Name + " (copy)";
        Config.Automations.Insert(index + 1, copy);
        return copy;
    }

    /// <summary>Inserts a copy of a profile right after it.</summary>
    public Profile? DuplicateProfile(string id)
    {
        var index = IndexOfProfile(id);
        if (index < 0)
            return null;
        var copy = ConfigCloner.Clone(Config.Profiles[index]);
        copy.Id = UniqueProfileId(copy.Id);
        copy.Name = string.IsNullOrWhiteSpace(copy.Name) ? null : copy.Name + " (copy)";
        Config.Profiles.Insert(index + 1, copy);
        return copy;
    }

    /// <summary>Removes an automation.</summary>
    public bool RemoveAutomation(string id)
    {
        var index = IndexOfAutomation(id);
        if (index < 0)
            return false;
        Config.Automations.RemoveAt(index);
        return true;
    }

    /// <summary>Removes a profile. Automations that use it become invalid until fixed.</summary>
    public bool RemoveProfile(string id)
    {
        var index = IndexOfProfile(id);
        if (index < 0)
            return false;
        Config.Profiles.RemoveAt(index);
        return true;
    }

    /// <summary>Moves an automation up (negative) or down (positive).</summary>
    public bool MoveAutomation(string id, int offset) => Move(Config.Automations, IndexOfAutomation(id), offset);

    /// <summary>Moves a profile up (negative) or down (positive).</summary>
    public bool MoveProfile(string id, int offset) => Move(Config.Profiles, IndexOfProfile(id), offset);

    /// <summary>Enables or disables an automation.</summary>
    public bool SetEnabled(string id, bool enabled)
    {
        var index = IndexOfAutomation(id);
        if (index < 0)
            return false;
        Config.Automations[index].Enabled = enabled;
        return true;
    }

    /// <summary>Replaces the automation with id <paramref name="id"/> (or adds it when missing).</summary>
    public void ReplaceAutomation(string id, Automation automation)
    {
        var index = IndexOfAutomation(id);
        if (index < 0)
            Config.Automations.Add(automation);
        else
            Config.Automations[index] = automation;
    }

    /// <summary>Replaces the profile with id <paramref name="id"/> (or adds it when missing).</summary>
    public void ReplaceProfile(string id, Profile profile)
    {
        var index = IndexOfProfile(id);
        if (index < 0)
            Config.Profiles.Add(profile);
        else
            Config.Profiles[index] = profile;
    }

    /// <summary>
    /// Parses and validates one automation written as YAML, in the context of this document (so profile
    /// references and id clashes are checked). <paramref name="originalId"/> is the id it replaces, if any.
    /// </summary>
    public (Automation? Automation, IReadOnlyList<ConfigIssue> Issues) ParseAutomation(string yaml, string? originalId)
    {
        var (automation, issues) = YamlConfigReader.ReadAutomationSnippet(yaml);
        if (automation is null)
            return (null, issues);

        if (string.IsNullOrWhiteSpace(automation.Id))
            automation.Id = originalId ?? UniqueAutomationId(ConfigValidator.Slug(automation.Name ?? "automation"));

        var context = new AutomationConfig
        {
            Profiles = Config.Profiles.Select(ConfigCloner.Clone).ToList(),
            Automations = [automation],
        };
        issues.AddRange(new ConfigValidator(_catalog).Validate(context, Scope)
            .Where(i => i.Message.StartsWith("Automation", StringComparison.Ordinal)));

        if (Config.Automations.Any(a => !SameId(a.Id, originalId) && SameId(a.Id, automation.Id)))
            issues.Add(new ConfigIssue(IssueSeverity.Error, $"Another automation already uses the id '{automation.Id}'.", automation.Location));
        return (automation, issues);
    }

    /// <summary>Parses and validates one profile written as YAML.</summary>
    public (Profile? Profile, IReadOnlyList<ConfigIssue> Issues) ParseProfile(string yaml, string? originalId)
    {
        var (profile, issues) = YamlConfigReader.ReadProfileSnippet(yaml);
        if (profile is null)
            return (null, issues);

        if (string.IsNullOrWhiteSpace(profile.Id))
            profile.Id = originalId ?? UniqueProfileId(ConfigValidator.Slug(profile.Name ?? "profile"));

        var context = new AutomationConfig { Profiles = [profile] };
        issues.AddRange(new ConfigValidator(_catalog).Validate(context, Scope));

        if (Config.Profiles.Any(p => !SameId(p.Id, originalId) && SameId(p.Id, profile.Id)))
            issues.Add(new ConfigIssue(IssueSeverity.Error, $"Another profile already uses the id '{profile.Id}'.", profile.Location));
        return (profile, issues);
    }

    private static bool SameId(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A free automation id based on <paramref name="baseId"/>.</summary>
    public string UniqueAutomationId(string baseId) => Unique(baseId, Config.Automations.Select(a => a.Id));

    /// <summary>A free profile id based on <paramref name="baseId"/>.</summary>
    public string UniqueProfileId(string baseId) => Unique(baseId, Config.Profiles.Select(p => p.Id));

    internal static string Unique(string baseId, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(baseId))
            baseId = "item";
        var id = baseId;
        for (var n = 2; taken.Contains(id); n++)
            id = $"{baseId}-{n}";
        return id;
    }

    private int IndexOfAutomation(string id) =>
        Config.Automations.FindIndex(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    private int IndexOfProfile(string id) =>
        Config.Profiles.FindIndex(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static bool Move<T>(List<T> list, int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || target < 0 || target >= list.Count || offset == 0)
            return false;
        var item = list[index];
        list.RemoveAt(index);
        list.Insert(target, item);
        return true;
    }
}
