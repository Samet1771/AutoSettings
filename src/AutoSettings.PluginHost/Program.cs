using System.Diagnostics;
using AutoSettings.Core.Plugins;
using AutoSettings.PluginHost;
using StreamJsonRpc;

// AutoSettings.PluginHost.exe --plugin <folder> --entry <assembly.dll> --parent <process id>
var options = args.Select((value, index) => (value, index))
    .Where(a => a.value.StartsWith("--", StringComparison.Ordinal) && a.index + 1 < args.Length)
    .ToDictionary(a => a.value, a => args[a.index + 1], StringComparer.OrdinalIgnoreCase);
if (!options.TryGetValue("--plugin", out var folder) || !options.TryGetValue("--entry", out var entry))
{
    Console.Error.WriteLine("Usage: AutoSettings.PluginHost --plugin <folder> --entry <assembly.dll> [--parent <pid>]");
    return 2;
}

// Standard output carries the protocol: whatever the plugin prints goes to standard error instead.
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
Console.SetOut(Console.Error);

// Exit with the parent, so no host is ever left behind.
if (options.TryGetValue("--parent", out var parentText) && int.TryParse(parentText, out var parentId))
{
    try
    {
        var parent = Process.GetProcessById(parentId);
        _ = parent.WaitForExitAsync().ContinueWith(_ => Environment.Exit(0), TaskScheduler.Default);
    }
    catch (ArgumentException)
    {
        return 0; // The parent is already gone.
    }
}

var server = new PluginHostServer(folder, entry);
using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(output, input, new SystemTextJsonFormatter()));
server.Callbacks = rpc.Attach<IPluginHostCallbacks>();
rpc.AddLocalRpcTarget(server);
rpc.StartListening();
try
{
    await rpc.Completion;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Connection to AutoSettings ended: " + ex.Message);
}
return 0;
