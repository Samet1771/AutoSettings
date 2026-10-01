using AutoSettings.Sdk;

namespace Example.Hello;

/// <summary>
/// A revertible action: sets a user environment variable. In a profile, the old value comes back when the
/// profile ends, because AutoSettings calls <see cref="CaptureAsync"/> first and <see cref="RestoreAsync"/> later.
/// </summary>
[PluginComponent("example.dotnet.set_variable",
    Title = "Set an environment variable",
    Description = "Sets a user environment variable. Programs started afterwards see the new value.",
    Example = "type: example.dotnet.set_variable\nname: FOCUS_MODE\nvalue: \"on\"")]
[Localized("tr", "Ortam değişkeni ayarla", Description = "Bir kullanıcı ortam değişkenini ayarlar.")]
[Field("name", FieldKind.String, Description = "The variable name.", Required = true, KeyField = true, Example = "FOCUS_MODE")]
[Field("value", FieldKind.String, Description = "The value. Leave empty to remove the variable.", Example = "on")]
public sealed class SetVariableAction : IRevertiblePluginAction
{
    // The marker for "the variable did not exist" in a snapshot.
    private const string Missing = "\u0000missing";

    public Task ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        var name = request.Parameters.GetString("name") ?? throw new PluginActionException("name is required");
        var value = request.Parameters.GetString("value");
        Set(name, string.IsNullOrEmpty(value) ? null : value);
        request.Log.Info($"{name} is now '{value}'.");
        return Task.CompletedTask;
    }

    public Task<string?> CaptureAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        var name = request.Parameters.GetString("name")!;
        return Task.FromResult<string?>(Environment.GetEnvironmentVariable(name, Target) ?? Missing);
    }

    public Task RestoreAsync(ActionRequest request, string? snapshot, CancellationToken cancellationToken)
    {
        var name = request.Parameters.GetString("name")!;
        Set(name, snapshot is null or Missing ? null : snapshot);
        return Task.CompletedTask;
    }

    // Tests set EXAMPLE_HELLO_PROCESS_SCOPE so they do not change the machine's user settings.
    private static EnvironmentVariableTarget Target =>
        Environment.GetEnvironmentVariable("EXAMPLE_HELLO_PROCESS_SCOPE") == "1" ? EnvironmentVariableTarget.Process : EnvironmentVariableTarget.User;

    private static void Set(string name, string? value)
    {
        if (name.Length == 0 || name.Contains('='))
            throw new PluginActionException($"'{name}' is not a valid variable name");
        Environment.SetEnvironmentVariable(name, value, Target);
    }

    internal static string? Get(string name) => Environment.GetEnvironmentVariable(name, Target);
}

/// <summary>A condition: true when a user environment variable has a value.</summary>
[PluginComponent("example.dotnet.variable_equals",
    Title = "Environment variable has a value",
    Description = "True when the user environment variable has this value (ignoring case).")]
[Localized("tr", "Ortam değişkeninin değeri")]
[Field("name", FieldKind.String, Description = "The variable name.", Required = true, Example = "FOCUS_MODE")]
[Field("value", FieldKind.String, Description = "The value to compare with.", Required = true, Example = "on")]
public sealed class VariableEqualsCondition : IPluginCondition
{
    public ValueTask<bool> EvaluateAsync(ConditionRequest request, CancellationToken cancellationToken)
    {
        var actual = SetVariableAction.Get(request.Parameters.GetString("name") ?? "");
        return ValueTask.FromResult(string.Equals(actual, request.Parameters.GetString("value"), StringComparison.OrdinalIgnoreCase));
    }
}
