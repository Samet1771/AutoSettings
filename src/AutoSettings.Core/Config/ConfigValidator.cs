using System.Text;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Model;
using AutoSettings.Core.Text;

namespace AutoSettings.Core.Config;

/// <summary>
/// Checks a configuration against the <see cref="ComponentCatalog"/> and normalizes parameter values
/// in place (for example <c>"30s"</c> becomes a <see cref="TimeSpan"/>).
/// </summary>
public sealed class ConfigValidator
{
    private readonly ComponentCatalog _catalog;

    /// <summary>Creates a validator for <paramref name="catalog"/> (the built-in catalog by default).</summary>
    public ConfigValidator(ComponentCatalog? catalog = null) => _catalog = catalog ?? ComponentCatalog.BuiltIn;

    /// <summary>
    /// Validates <paramref name="config"/> for use in a personal (<see cref="ExecutionScope.User"/>) or
    /// machine (<see cref="ExecutionScope.Machine"/>) automation file. Missing ids are generated.
    /// </summary>
    public List<ConfigIssue> Validate(AutomationConfig config, ExecutionScope scope)
    {
        var issues = new List<ConfigIssue>();
        if (config.Version != Product.ConfigVersion)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error,
                $"Unsupported file version {config.Version}. This version of {Product.Name} reads version {Product.ConfigVersion}."));
        }

        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.Profiles.Count; i++)
        {
            var profile = config.Profiles[i];
            var label = string.IsNullOrWhiteSpace(profile.Name) && string.IsNullOrWhiteSpace(profile.Id)
                ? $"Profile {i + 1}"
                : $"Profile '{profile.DisplayName}'";

            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                if (string.IsNullOrWhiteSpace(profile.Name))
                {
                    issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: 'id' is required.", profile.Location));
                    continue;
                }
                profile.Id = UniqueId(Slug(profile.Name!), profileIds);
            }
            if (!profileIds.Add(profile.Id))
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: another profile already uses the id '{profile.Id}'.", profile.Location));
            if (profile.Actions.Count == 0)
                issues.Add(new ConfigIssue(IssueSeverity.Warning, $"{label}: has no actions.", profile.Location));

            for (var j = 0; j < profile.Actions.Count; j++)
                ValidateComponent(ComponentKind.Action, profile.Actions[j], $"{label}, action {j + 1}", scope, profile.Location, issues, profileIds: null);
        }

        var automationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.Automations.Count; i++)
        {
            var automation = config.Automations[i];
            var label = string.IsNullOrWhiteSpace(automation.Name) && string.IsNullOrWhiteSpace(automation.Id)
                ? $"Automation {i + 1}"
                : $"Automation '{automation.DisplayName}'";

            if (string.IsNullOrWhiteSpace(automation.Id))
                automation.Id = UniqueId(string.IsNullOrWhiteSpace(automation.Name) ? $"automation-{i + 1}" : Slug(automation.Name!), automationIds);
            if (!automationIds.Add(automation.Id))
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: another automation already uses the id '{automation.Id}'.", automation.Location));

            if (automation.Triggers.Count == 0)
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: needs at least one trigger.", automation.Location));
            if (automation.Actions.Count == 0)
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: needs at least one action.", automation.Location));

            for (var j = 0; j < automation.Triggers.Count; j++)
                ValidateComponent(ComponentKind.Trigger, automation.Triggers[j], $"{label}, trigger {j + 1}", scope, automation.Location, issues, profileIds);
            for (var j = 0; j < automation.Conditions.Count; j++)
                ValidateComponent(ComponentKind.Condition, automation.Conditions[j], $"{label}, condition {j + 1}", scope, automation.Location, issues, profileIds);
            for (var j = 0; j < automation.Actions.Count; j++)
                ValidateComponent(ComponentKind.Action, automation.Actions[j], $"{label}, action {j + 1}", scope, automation.Location, issues, profileIds);
        }

        return issues;
    }

    /// <summary>
    /// Converts the parameters of a single component to their typed form (used for actions received over IPC).
    /// </summary>
    public List<ConfigIssue> NormalizeComponent(ComponentKind kind, ComponentConfig component, ExecutionScope scope)
    {
        var issues = new List<ConfigIssue>();
        ValidateComponent(kind, component, component.Type, scope, null, issues, profileIds: null, checkProfileReferences: false);
        return issues;
    }

    private void ValidateComponent(
        ComponentKind kind,
        ComponentConfig component,
        string label,
        ExecutionScope scope,
        SourceLocation? fallbackLocation,
        List<ConfigIssue> issues,
        HashSet<string>? profileIds,
        bool checkProfileReferences = true)
    {
        var location = component.Location ?? fallbackLocation;
        var kindName = kind.ToString().ToLowerInvariant();
        label = $"{label} ({component.Type})";

        var descriptor = _catalog.Find(kind, component.Type);
        if (descriptor is null)
        {
            var known = _catalog.OfKind(kind).Select(d => d.Type);
            issues.Add(new ConfigIssue(IssueSeverity.Error,
                $"{label}: unknown {kindName} type '{component.Type}'.{Suggestions.DidYouMean(component.Type, known)}", location));
            return;
        }

        var errorsBefore = issues.Count(i => i.Severity == IssueSeverity.Error);

        if (kind != ComponentKind.Action)
        {
            var required = scope == ExecutionScope.User ? ScopeSupport.User : ScopeSupport.Machine;
            if (!descriptor.AvailableIn.HasFlag(required))
            {
                var where = descriptor.AvailableIn == ScopeSupport.User ? "personal" : "machine";
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: can only be used in {where} automations.", location));
            }
        }

        foreach (var key in component.Parameters.Keys.ToList())
        {
            var field = descriptor.Field(key);
            if (field is null)
            {
                var known = descriptor.Fields.Select(f => f.Name);
                issues.Add(new ConfigIssue(IssueSeverity.Warning,
                    $"{label}: unknown field '{key}' is ignored.{Suggestions.DidYouMean(key, known)}", location));
                continue;
            }

            var raw = component.Parameters[key];
            if (raw is null)
                continue;

            if (field.Type == FieldType.ConditionList)
            {
                var nested = raw as List<ComponentConfig>
                    ?? (raw as IEnumerable<ComponentConfig>)?.ToList()
                    ?? YamlConfigReader.ReadComponents(raw, ComponentKind.Condition, label, location, issues);
                for (var i = 0; i < nested.Count; i++)
                    ValidateComponent(ComponentKind.Condition, nested[i], $"{label}, condition {i + 1}", scope, location, issues, profileIds, checkProfileReferences);
                component.Parameters[key] = nested;
                continue;
            }

            if (ValueConverter.TryConvert(raw, field, out var typed, out var error))
                component.Parameters[key] = typed;
            else
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: invalid '{key}': {error}.", location));
        }

        foreach (var field in descriptor.Fields.Where(f => f.Required && !component.Has(f.Name)))
            issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: '{field.Name}' is required.", location));

        // Custom checks only make sense once the individual fields are valid.
        if (descriptor.Validate is not null && issues.Count(i => i.Severity == IssueSeverity.Error) == errorsBefore)
        {
            foreach (var message in descriptor.Validate(component))
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: {message}.", location));
        }

        if (kind == ComponentKind.Action && scope == ExecutionScope.User && descriptor.ResolveRunsAs(component) == ExecutionScope.Machine)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error,
                $"{label}: needs administrator rights with these settings, so it can only be used in machine automations.", location));
        }

        if (checkProfileReferences && kind == ComponentKind.Action
            && component.Type is BuiltInActions.ProfileApply or BuiltInActions.ProfileRevert)
        {
            var profile = component.GetString("profile");
            if (profileIds is null)
            {
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: a profile cannot apply or revert other profiles.", location));
            }
            else if (profile is not null && !profileIds.Contains(profile))
            {
                issues.Add(new ConfigIssue(IssueSeverity.Error,
                    $"{label}: there is no profile with the id '{profile}' in this file.{Suggestions.DidYouMean(profile, profileIds)}", location));
            }
        }
    }

    /// <summary>Turns a name into an id: lower case, letters, digits and dashes.</summary>
    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "item" : slug;
    }

    private static string UniqueId(string baseId, HashSet<string> taken)
    {
        var id = baseId;
        for (var n = 2; taken.Contains(id); n++)
            id = $"{baseId}-{n}";
        return id;
    }
}
