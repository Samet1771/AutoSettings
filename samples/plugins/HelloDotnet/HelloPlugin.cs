using AutoSettings.Sdk;

namespace Example.Hello;

/// <summary>The entry point: AutoSettings creates it once and asks for the components.</summary>
public sealed class HelloPlugin : IPlugin
{
    public void Configure(IPluginBuilder builder) => builder
        .AddAction(new SetVariableAction())
        .AddCondition(new VariableEqualsCondition())
        .AddTrigger(new FolderWatcher());
}
