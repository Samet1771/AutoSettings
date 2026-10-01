using System.ComponentModel;
using System.Diagnostics;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using AutoSettings.Core.Plugins;
using AutoSettings.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace AutoSettings.Platform.Plugins;

/// <summary>
/// Runs .NET plugins, each in its own <c>AutoSettings.PluginHost.exe</c> process. The host starts on first use and
/// is restarted after a crash; after <see cref="MaxCrashes"/> crashes in <see cref="CrashWindow"/> the plugin is
/// turned off until AutoSettings restarts or the plugin changes.
/// </summary>
public sealed class DotnetBackend : IPluginBackend, IDisposable
{
    /// <summary>Crashes within <see cref="CrashWindow"/> after which a plugin is turned off.</summary>
    public const int MaxCrashes = 3;

    /// <summary>The window for <see cref="MaxCrashes"/>.</summary>
    public static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(10);

    private readonly string _hostPath;
    private readonly ILogger _logger;
    private readonly Dictionary<string, PluginHostClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the backend.</summary>
    /// <param name="hostPath">The host executable; by default <c>AutoSettings.PluginHost.exe</c> next to the running program.</param>
    /// <param name="logger">Diagnostics.</param>
    public DotnetBackend(string? hostPath = null, ILogger? logger = null)
    {
        _hostPath = hostPath ?? Path.Combine(AppContext.BaseDirectory, "AutoSettings.PluginHost.exe");
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool Supports(PluginKind kind) => kind == PluginKind.Dotnet;

    /// <inheritdoc />
    public IActionHandler CreateAction(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context) =>
        new HostActionHandler(Client(plugin, context), component);

    /// <inheritdoc />
    public IConditionHandler CreateCondition(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context) =>
        new HostConditionHandler(Client(plugin, context), component);

    /// <inheritdoc />
    public IDisposable? StartTriggers(InstalledPlugin plugin, PluginHostContext context, Action<PluginEventReport> raise)
    {
        if (plugin.Manifest!.Components.All(c => c.Kind != ComponentKind.Trigger))
            return null;
        return Client(plugin, context).StartTriggers(raise);
    }

    /// <inheritdoc />
    public void Unload(InstalledPlugin plugin)
    {
        PluginHostClient? client;
        lock (_clients)
        {
            if (!_clients.Remove(plugin.Directory, out client))
                return;
        }
        client.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<PluginHostClient> clients;
        lock (_clients)
        {
            clients = [.. _clients.Values];
            _clients.Clear();
        }
        foreach (var client in clients)
            client.Dispose();
    }

    private PluginHostClient Client(InstalledPlugin plugin, PluginHostContext context)
    {
        lock (_clients)
        {
            if (!_clients.TryGetValue(plugin.Directory, out var client))
                _clients[plugin.Directory] = client = new PluginHostClient(plugin, context, _hostPath, _logger);
            return client;
        }
    }

    private sealed class HostActionHandler(PluginHostClient client, ManifestComponent component) : IRevertibleActionHandler
    {
        public string Type => component.Type;

        public Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken) =>
            client.InvokeAsync(component, PluginOperation.Apply, action, context.Event, context.User, context.AutomationName, null, cancellationToken);

        public Task<string?> CaptureAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken) =>
            client.InvokeAsync(component, PluginOperation.Capture, action, context.Event, context.User, context.AutomationName, null, cancellationToken);

        public Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext context, CancellationToken cancellationToken) =>
            client.InvokeAsync(component, PluginOperation.Restore, action, context.Event, context.User, context.AutomationName, snapshot, cancellationToken);
    }

    private sealed class HostConditionHandler(PluginHostClient client, ManifestComponent component) : IConditionHandler
    {
        public string Type => component.Type;

        public async ValueTask<bool> EvaluateAsync(ComponentConfig condition, ConditionContext context, CancellationToken cancellationToken)
        {
            var result = await client.InvokeAsync(component, PluginOperation.Evaluate, condition, context.Event, context.Event?.User ?? context.CurrentUser, null, null, cancellationToken).ConfigureAwait(false);
            return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>One running plugin host and the connection to it.</summary>
internal sealed class PluginHostClient : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(60);

    private readonly InstalledPlugin _plugin;
    private readonly PluginHostContext _context;
    private readonly string _hostPath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly Queue<DateTimeOffset> _crashes = new();
    private readonly Queue<string> _errorTail = new();
    private Process? _process;
    private JsonRpc? _rpc;
    private IPluginHostRpc? _proxy;
    private Action<PluginEventReport>? _raise;
    private bool _broken;
    private bool _disposed;

    public PluginHostClient(InstalledPlugin plugin, PluginHostContext context, string hostPath, ILogger logger)
    {
        _plugin = plugin;
        _context = context;
        _hostPath = hostPath;
        _logger = logger;
    }

    private string Name => _plugin.DisplayName;

    public async Task<string?> InvokeAsync(
        ManifestComponent component,
        PluginOperation operation,
        ComponentConfig config,
        Core.Events.SystemEvent? triggerEvent,
        Core.Events.UserInfo? user,
        string? automationName,
        string? snapshot,
        CancellationToken cancellationToken)
    {
        var proxy = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var request = PluginRequest.Build(_plugin, component.Type, operation, config.Parameters, triggerEvent, user, automationName, snapshot,
            _context.StateDirectoryFor(_plugin.Id));
        var timeout = component.Timeout ?? DefaultCallTimeout;
        try
        {
            return await proxy.InvokeAsync(component.Type, PluginRequest.Name(operation), request, cancellationToken)
                .WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A plugin that does not answer may be stuck for good: start a fresh host next time.
            _context.Log.Warning(ActivitySources.Plugin, $"{Name} did not answer within {ValueConverter.FormatDuration(timeout)}; it was restarted.");
            Stop();
            throw new ActionFailedException($"{component.Type} did not finish within {ValueConverter.FormatDuration(timeout)}");
        }
        catch (RemoteInvocationException ex)
        {
            throw new ActionFailedException(ex.Message, ex);
        }
        catch (ConnectionLostException ex)
        {
            throw new ActionFailedException($"{Name} stopped while running {component.Type}", ex);
        }
    }

    public IDisposable StartTriggers(Action<PluginEventReport> raise)
    {
        _raise = raise;
        _ = Task.Run(StartTriggersAsync);
        return new Stopper(this);
    }

    public void Dispose()
    {
        _disposed = true;
        _raise = null;
        Stop();
    }

    private async Task StartTriggersAsync()
    {
        try
        {
            var proxy = await EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);
            await proxy.StartTriggersAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!_disposed && _raise is not null)
                _context.Log.Error(ActivitySources.Plugin, $"Could not start the triggers of {Name}: {ex.Message}");
        }
    }

    private void StopTriggers()
    {
        _raise = null;
        if (_proxy is { } proxy)
            _ = proxy.StopTriggersAsync(CancellationToken.None).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task<IPluginHostRpc> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_proxy is { } running && _process is { HasExited: false })
            return running;
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_proxy is { } started && _process is { HasExited: false })
                return started;
            if (_disposed)
                throw new ActionFailedException($"{Name} was unloaded");
            if (_broken)
                throw new ActionFailedException($"{Name} was turned off after crashing {DotnetBackend.MaxCrashes} times; update it or restart {Core.Product.Name}");
            return await StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<IPluginHostRpc> StartAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_hostPath))
            throw new ActionFailedException($"{Path.GetFileName(_hostPath)} is missing; reinstall {Core.Product.Name}");

        var startInfo = new ProcessStartInfo(_hostPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _plugin.Directory,
        };
        foreach (var argument in new[] { "--plugin", _plugin.Directory, "--entry", _plugin.Manifest!.Entry!, "--parent", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            startInfo.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new ActionFailedException($"the host for {Name} could not be started");
        }
        catch (Win32Exception ex)
        {
            throw new ActionFailedException($"the host for {Name} could not be started: {ex.Message}", ex);
        }
        process.EnableRaisingEvents = true;
        process.ErrorDataReceived += (_, e) => OnErrorLine(e.Data);
        process.BeginErrorReadLine();

        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, new SystemTextJsonFormatter()));
        rpc.AddLocalRpcTarget(new Callbacks(this));
        var proxy = rpc.Attach<IPluginHostRpc>();
        rpc.StartListening();
        process.Exited += (_, _) => OnExited(process);
        _process = process;
        _rpc = rpc;

        PluginHostHello hello;
        try
        {
            hello = await proxy.HelloAsync(SdkInfo.ProtocolVersion, cancellationToken).WaitAsync(StartTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or ConnectionLostException or RemoteInvocationException)
        {
            Stop();
            throw new ActionFailedException($"{Name} could not start: {(ex is TimeoutException ? "the plugin host did not answer" : ErrorTail() ?? ex.Message)}", ex);
        }

        foreach (var problem in hello.Problems)
            _context.Log.Error(ActivitySources.Plugin, $"{Name}: {problem}");
        var declared = _plugin.Manifest.Components.Select(c => c.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = declared.Except(hello.Components, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0)
            _context.Log.Warning(ActivitySources.Plugin, $"{Name}: plugin.yaml lists {string.Join(", ", missing)}, but the plugin does not register them. Pack the plugin again.");
        _proxy = proxy;
        _logger.LogInformation("Started plugin host for {Plugin} (pid {Pid})", _plugin.Id, process.Id);

        if (_raise is not null)
            await proxy.StartTriggersAsync(cancellationToken).ConfigureAwait(false);
        return proxy;
    }

    private void OnExited(Process process)
    {
        if (!ReferenceEquals(process, _process) || _disposed)
            return;
        _proxy = null;
        var now = DateTimeOffset.Now;
        lock (_crashes)
        {
            _crashes.Enqueue(now);
            while (_crashes.Count > 0 && now - _crashes.Peek() > DotnetBackend.CrashWindow)
                _crashes.Dequeue();
            _broken = _crashes.Count >= DotnetBackend.MaxCrashes;
        }
        var detail = ErrorTail() is { } tail ? $": {tail}" : "";
        if (_broken)
        {
            _context.Log.Error(ActivitySources.Plugin,
                $"{Name} stopped unexpectedly {DotnetBackend.MaxCrashes} times in {ValueConverter.FormatDuration(DotnetBackend.CrashWindow)} and was turned off{detail}. Update it or restart {Core.Product.Name}.");
            return;
        }
        _context.Log.Warning(ActivitySources.Plugin, $"{Name} stopped unexpectedly (exit code {SafeExitCode(process)}){detail}. It starts again when needed.");
        if (_raise is not null)
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => StartTriggersAsync(), TaskScheduler.Default);
    }

    private void Stop()
    {
        var process = _process;
        var rpc = _rpc;
        _process = null;
        _rpc = null;
        _proxy = null;
        rpc?.Dispose();
        if (process is null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
        process.Dispose();
    }

    private void OnErrorLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        _logger.LogInformation("[{Plugin}] {Line}", _plugin.Id, line);
        lock (_errorTail)
        {
            _errorTail.Enqueue(line);
            while (_errorTail.Count > 10)
                _errorTail.Dequeue();
        }
    }

    private string? ErrorTail()
    {
        lock (_errorTail)
        {
            if (_errorTail.Count == 0)
                return null;
            var text = string.Join(" ", _errorTail);
            return text.Length <= 400 ? text : "…" + text[^400..];
        }
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    private sealed class Stopper(PluginHostClient client) : IDisposable
    {
        public void Dispose() => client.StopTriggers();
    }

    private sealed class Callbacks(PluginHostClient client) : IPluginHostCallbacks
    {
        public Task OnEventAsync(string name, Dictionary<string, string> data, string? user)
        {
            if (client._raise is not { } raise)
                return Task.CompletedTask;
            if (!name.StartsWith(client._plugin.Id + ".", StringComparison.OrdinalIgnoreCase))
            {
                client._context.Log.Warning(ActivitySources.Plugin, $"{client.Name} raised '{name}', which is not one of its events (they start with '{client._plugin.Id}.').");
                return Task.CompletedTask;
            }
            raise(new PluginEventReport(name, data, user));
            return Task.CompletedTask;
        }

        public Task OnLogAsync(string level, string message)
        {
            var text = $"{client.Name}: {message}";
            switch (level)
            {
                case "error": client._context.Log.Error(ActivitySources.Plugin, text); break;
                case "warning": client._context.Log.Warning(ActivitySources.Plugin, text); break;
                default: client._context.Log.Info(ActivitySources.Plugin, text); break;
            }
            return Task.CompletedTask;
        }
    }
}
