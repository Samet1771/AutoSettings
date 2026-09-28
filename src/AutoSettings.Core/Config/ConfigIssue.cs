using AutoSettings.Core.Model;

namespace AutoSettings.Core.Config;

/// <summary>How serious a configuration problem is.</summary>
public enum IssueSeverity
{
    /// <summary>Suspicious but usable (for example an unknown field that is ignored).</summary>
    Warning,
    /// <summary>The file cannot be used until this is fixed.</summary>
    Error,
}

/// <summary>A problem found while reading or validating a configuration file.</summary>
/// <param name="Severity">Warning or error.</param>
/// <param name="Message">User-facing message, including where the problem is (e.g. "Automation 'Gaming', trigger 1").</param>
/// <param name="Location">Line and column in the YAML file, when known.</param>
public sealed record ConfigIssue(IssueSeverity Severity, string Message, SourceLocation? Location = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        Location is { } l ? $"{Severity} ({l}): {Message}" : $"{Severity}: {Message}";
}

/// <summary>The result of loading a configuration file.</summary>
/// <param name="Config">The configuration. Only use it when <see cref="HasErrors"/> is false.</param>
/// <param name="Issues">Errors and warnings.</param>
public sealed record ConfigLoadResult(AutomationConfig Config, IReadOnlyList<ConfigIssue> Issues)
{
    /// <summary>Whether any issue is an error.</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);

    /// <summary>Only the errors.</summary>
    public IEnumerable<ConfigIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);

    /// <summary>Only the warnings.</summary>
    public IEnumerable<ConfigIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);
}
