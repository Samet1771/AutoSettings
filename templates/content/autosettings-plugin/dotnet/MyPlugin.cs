using AutoSettings.Sdk;

namespace MyPlugin;

/// <summary>The entry point: AutoSettings creates it once and asks for the components.</summary>
public sealed class Plugin : IPlugin
{
    public void Configure(IPluginBuilder builder) => builder
        .AddAction(new SayHelloAction());
}

/// <summary>An action. Build, then run: autosettings-plugin pack bin\Release\net10.0 --out dist</summary>
[PluginComponent("mypublisher.myplugin.say_hello",
    Title = "Say hello",
    Description = "Writes a greeting to the Activity page.",
    Example = "type: mypublisher.myplugin.say_hello\nname: \"{{ user }}\"")]
[Field("name", FieldKind.String, Description = "Who to greet. Placeholders such as {{ user }} work.", Required = true, Example = "{{ user }}")]
public sealed class SayHelloAction : IPluginAction
{
    public Task ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        var name = request.Parameters.GetString("name");
        if (string.IsNullOrWhiteSpace(name))
            throw new PluginActionException("name is empty");
        request.Log.Info($"Hello, {name}!");
        return Task.CompletedTask;
    }
}
