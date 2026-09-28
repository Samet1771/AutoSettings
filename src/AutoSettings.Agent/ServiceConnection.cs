using System.IO.Pipes;
using AutoSettings.Core;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Ipc;
using AutoSettings.Platform.Monitoring;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace AutoSettings.Agent;

/// <summary>Keeps a JSON-RPC connection to the service's named pipe, reconnecting as needed.</summary>
public sealed class ServiceConnection : IAsyncDisposable
{
    private readonly IAgentApi _target;
    private readonly ActivityLog _activity;
    private readonly int _sessionId;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;
    private volatile IServiceApi? _service;
    private volatile bool _connected;

    public ServiceConnection(IAgentApi target, ActivityLog activity, int sessionId, ILogger logger)
    {
        _target = target;
        _activity = activity;
        _sessionId = sessionId;
        _logger = logger;
    }

    public bool IsConnected => _connected;

    /// <summary>The process monitor the service reported.</summary>
    public string? ProcessMonitorName { get; private set; }

    /// <summary>Raised with true when connected and false when disconnected.</summary>
    public event Action<bool>? ConnectionChanged;

    public void Start() => _loop = Task.Run(() => RunAsync(_stopping.Token));

    /// <summary>Tells the service the user closed the agent (so it is not restarted).</summary>
    public async Task NotifyExitingAsync()
    {
        try
        {
            if (_service is { } service)
                await service.AgentExitingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not notify the service");
        }
    }

    /// <summary>The machine activity log, or an empty list when not connected.</summary>
    public async Task<IReadOnlyList<ActivityEntry>> GetMachineActivityAsync()
    {
        try
        {
            if (_service is { } service)
                return await service.GetMachineActivityAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the machine activity");
        }
        return [];
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        var warned = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", Product.ServicePipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);

                // Only the real service (running in session 0) may drive this agent.
                var serverPid = PipeIdentity.ServerProcessId(pipe);
                if (serverPid is null || ProcessQuery.TryGetSessionId(serverPid.Value) != 0)
                {
                    _logger.LogWarning("Refusing pipe server {Pid}: it is not a Windows service", serverPid);
                    throw new UnauthorizedAccessException("The pipe is not owned by the AutoSettings service.");
                }

                using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, new SystemTextJsonFormatter()));
                rpc.AddLocalRpcTarget(_target);
                var service = rpc.Attach<IServiceApi>();
                rpc.StartListening();

                var hello = await service.RegisterAgentAsync(
                    new AgentHello(_sessionId, Environment.ProcessId, typeof(ServiceConnection).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"),
                    cancellationToken).ConfigureAwait(false);
                ProcessMonitorName = hello.ProcessMonitor;
                _service = service;
                SetConnected(true);
                _activity.Info(ActivitySources.Connection, $"Connected to the {Product.Name} service.");
                delay = TimeSpan.FromSeconds(2);
                warned = false;

                await rpc.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!warned)
                    _logger.LogInformation("Service not reachable: {Message}", ex.Message);
                warned = true;
            }
            finally
            {
                _service = null;
                if (_connected)
                {
                    SetConnected(false);
                    _activity.Warning(ActivitySources.Connection, $"Lost the connection to the {Product.Name} service; reconnecting.");
                }
            }

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }

    private void SetConnected(bool connected)
    {
        _connected = connected;
        ConnectionChanged?.Invoke(connected);
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _stopping.Dispose();
    }
}
