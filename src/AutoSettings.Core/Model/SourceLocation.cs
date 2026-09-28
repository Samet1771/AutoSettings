namespace AutoSettings.Core.Model;

/// <summary>A 1-based line/column position in a YAML file, used for error messages.</summary>
/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column number.</param>
public readonly record struct SourceLocation(int Line, int Column)
{
    /// <inheritdoc />
    public override string ToString() => $"line {Line}, column {Column}";
}
