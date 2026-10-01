using System.Reflection;
using System.Text.Json;
using AutoSettings.Core.Plugins;
using AutoSettings.Sdk;

namespace AutoSettings.PluginHost;

/// <summary>Loads the plugin and answers AutoSettings' calls.</summary>
internal sealed class PluginHostServer : IPluginHostRpc
{
    private readonly Dictionary<string, IPluginAction> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IPluginCondition> _conditions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IPluginTrigger> _triggers = [];
    private readonly List<string> _problems = [];
    private readonly object _gate = new();
    private CancellationTokenSource? _triggerStop;

    public PluginHostServer(string folder, string entry)
    {
        try
        {
            Load(Path.Combine(folder, entry));
        }
        catch (Exception ex)
        {
            _problems.Add($"Could not load {entry}: {Unwrap(ex).Message}");
        }
    }

    public IPluginHostCallbacks? Callbacks { get; set; }

    public Task<PluginHostHello> HelloAsync(int protocolVersion, CancellationToken cancellationToken)
    {
        var problems = new List<string>(_problems);
        if (protocolVersion != SdkInfo.ProtocolVersion)
            problems.Add($"AutoSettings uses protocol {protocolVersion} but this plugin host uses {SdkInfo.ProtocolVersion}.");
        var components = _actions.Keys.Concat(_conditions.Keys).Concat(_triggers.SelectMany(TypesOf)).ToList();
        return Task.FromResult(new PluginHostHello(SdkInfo.ProtocolVersion, SdkInfo.Version, components, problems));
    }

    public async Task<string?> InvokeAsync(string component, string operation, string requestJson, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(requestJson);
        var root = document.RootElement;
        var log = new CallbackLog(this);
        var parameters = root.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object
            ? Parameters.FromJson(p.GetRawText())
            : Parameters.Empty;
        var user = ReadUser(root, "user");
        var triggerEvent = ReadEvent(root);
        var automation = root.TryGetProperty("automation", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;

        if (operation == "evaluate")
        {
            var condition = _conditions.GetValueOrDefault(component) ?? throw new PluginActionException($"{component} is not a condition of this plugin");
            var result = await condition.EvaluateAsync(new ConditionRequest(parameters, log) { Event = triggerEvent, User = user }, cancellationToken);
            return result ? "true" : "false";
        }

        var action = _actions.GetValueOrDefault(component) ?? throw new PluginActionException($"{component} is not an action of this plugin");
        var request = new ActionRequest(parameters, log) { Event = triggerEvent, User = user, AutomationName = automation };
        switch (operation)
        {
            case "apply":
                await action.ExecuteAsync(request, cancellationToken);
                return null;
            case "capture":
                return action is IRevertiblePluginAction capture ? await capture.CaptureAsync(request, cancellationToken) : null;
            case "restore":
                if (action is IRevertiblePluginAction restore)
                {
                    var snapshot = root.TryGetProperty("snapshot", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    await restore.RestoreAsync(request, snapshot, cancellationToken);
                }
                return null;
            default:
                throw new PluginActionException($"unknown operation '{operation}'");
        }
    }

    public Task StartTriggersAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_triggerStop is not null)
                return Task.CompletedTask;
            _triggerStop = new CancellationTokenSource();
            var sink = new Sink(this);
            foreach (var trigger in _triggers)
            {
                var stop = _triggerStop.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await trigger.RunAsync(sink, stop);
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        Log("error", $"{string.Join(", ", TypesOf(trigger))} stopped: {ex.Message}");
                    }
                }, CancellationToken.None);
            }
        }
        return Task.CompletedTask;
    }

    public Task StopTriggersAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _triggerStop?.Cancel();
            _triggerStop?.Dispose();
            _triggerStop = null;
        }
        return Task.CompletedTask;
    }

    private void Load(string path)
    {
        var assembly = PluginLoadContext.LoadPlugin(path);
        var pluginTypes = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(t)).ToList();
        if (pluginTypes.Count != 1)
        {
            _problems.Add(pluginTypes.Count == 0
                ? $"{Path.GetFileName(path)} has no public class that implements IPlugin."
                : $"{Path.GetFileName(path)} has more than one class that implements IPlugin: {string.Join(", ", pluginTypes.Select(t => t.FullName))}.");
            return;
        }
        var plugin = (IPlugin)Activator.CreateInstance(pluginTypes[0])!;
        plugin.Configure(new Builder(this));
    }

    private void Register(object component)
    {
        var types = TypesOf(component).ToList();
        if (types.Count == 0)
        {
            _problems.Add($"{component.GetType().FullName} was added but has no [PluginComponent] attribute.");
            return;
        }
        switch (component)
        {
            case IPluginAction action:
                foreach (var type in types)
                    _actions[type] = action;
                break;
            case IPluginCondition condition:
                foreach (var type in types)
                    _conditions[type] = condition;
                break;
            case IPluginTrigger trigger:
                _triggers.Add(trigger);
                break;
        }
    }

    private static IEnumerable<string> TypesOf(object component) =>
        component.GetType().GetCustomAttributes<PluginComponentAttribute>(inherit: false).Select(a => a.Type);

    private void Log(string level, string message)
    {
        if (Callbacks is { } callbacks)
            _ = callbacks.OnLogAsync(level, message);
        else
            Console.Error.WriteLine($"[{level}] {message}");
    }

    private static PluginUser? ReadUser(JsonElement element, string property) =>
        element.TryGetProperty(property, out var u) && u.ValueKind == JsonValueKind.Object && u.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? new PluginUser(name.GetString()!, Text(u, "domain"), Text(u, "sid"))
            : null;

    private static PluginEventInfo? ReadEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var e) || e.ValueKind != JsonValueKind.Object)
            return null;
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        if (e.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in d.EnumerateObject())
                data[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();
        }
        return new PluginEventInfo(Text(e, "name") ?? "")
        {
            SessionId = e.TryGetProperty("session", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : null,
            User = ReadUser(e, "user"),
            AppName = Text(e, "app"),
            AppPath = Text(e, "app_path"),
            WindowTitle = Text(e, "window_title"),
            Data = data,
        };
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Exception Unwrap(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;

    private sealed class Builder(PluginHostServer server) : IPluginBuilder
    {
        public IPluginBuilder AddAction(IPluginAction action)
        {
            server.Register(action);
            return this;
        }

        public IPluginBuilder AddCondition(IPluginCondition condition)
        {
            server.Register(condition);
            return this;
        }

        public IPluginBuilder AddTrigger(IPluginTrigger trigger)
        {
            server.Register(trigger);
            return this;
        }
    }

    private sealed class CallbackLog(PluginHostServer server) : IPluginLog
    {
        public void Info(string message) => server.Log("info", message);

        public void Warning(string message) => server.Log("warning", message);

        public void Error(string message) => server.Log("error", message);
    }

    private sealed class Sink(PluginHostServer server) : ITriggerSink
    {
        public IPluginLog Log { get; } = new CallbackLog(server);

        public void Raise(string eventName, IReadOnlyDictionary<string, string>? data = null, PluginUser? user = null)
        {
            if (server.Callbacks is { } callbacks)
                _ = callbacks.OnEventAsync(eventName, data?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, string>(), user?.Name);
        }
    }
}
