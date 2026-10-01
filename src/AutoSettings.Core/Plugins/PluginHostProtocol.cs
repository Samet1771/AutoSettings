namespace AutoSettings.Core.Plugins;

/// <summary>
/// What a plugin host (<c>AutoSettings.PluginHost.exe</c>, one process per .NET plugin) offers to AutoSettings, over
/// JSON-RPC on its standard input and output.
/// </summary>
public interface IPluginHostRpc
{
    /// <summary>Checks that both sides speak the same protocol and returns what the plugin registered.</summary>
    Task<PluginHostHello> HelloAsync(int protocolVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operation"/> (<c>apply</c>, <c>capture</c>, <c>restore</c> or <c>evaluate</c>) on a component.
    /// <paramref name="requestJson"/> is the same request script plugins get (<see cref="PluginRequest.Build"/>).
    /// Returns the snapshot for <c>capture</c>, <c>true</c>/<c>false</c> for <c>evaluate</c>, otherwise <c>null</c>.
    /// A failure is reported as an error whose message is shown to the user.
    /// </summary>
    Task<string?> InvokeAsync(string component, string operation, string requestJson, CancellationToken cancellationToken);

    /// <summary>Starts the plugin's triggers. Events come back through <see cref="IPluginHostCallbacks.OnEventAsync"/>.</summary>
    Task StartTriggersAsync(CancellationToken cancellationToken);

    /// <summary>Stops the plugin's triggers.</summary>
    Task StopTriggersAsync(CancellationToken cancellationToken);
}

/// <summary>What AutoSettings offers to a plugin host.</summary>
public interface IPluginHostCallbacks
{
    /// <summary>A trigger raised an event.</summary>
    Task OnEventAsync(string name, Dictionary<string, string> data, string? user);

    /// <summary>The plugin wrote to the activity log (<paramref name="level"/> is info, warning or error).</summary>
    Task OnLogAsync(string level, string message);
}

/// <summary>The answer to <see cref="IPluginHostRpc.HelloAsync"/>.</summary>
/// <param name="ProtocolVersion">The host's protocol version.</param>
/// <param name="SdkVersion">The SDK version the host provides.</param>
/// <param name="Components">The component types the plugin registered.</param>
/// <param name="Problems">Problems found while loading the plugin (shown in the activity log).</param>
public sealed record PluginHostHello(int ProtocolVersion, string SdkVersion, List<string> Components, List<string> Problems);
