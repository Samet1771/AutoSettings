using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using AutoSettings.Core;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Ipc;
using AutoSettings.Core.Updates;
using AutoSettings.Platform.Monitoring;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace AutoSettings.Service;

/// <summary>Accepts agent connections on the service's named pipe and speaks JSON-RPC with them.</summary>
public sealed class PipeServer : BackgroundService
{
    private readonly AgentHub _hub;
    private readonly AgentSupervisor _supervisor;
    private readonly ActivityLog _activity;
    private readonly MachineHost _machine;
    private readonly UpdateService _updates;
    private readonly ILogger<PipeServer> _logger;

    public PipeServer(AgentHub hub, AgentSupervisor supervisor, ActivityLog activity, MachineHost machine, UpdateService updates, ILogger<PipeServer> logger)
    {
        _hub = hub;
        _supervisor = supervisor;
        _activity = activity;
        _machine = machine;
        _updates = updates;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot create the agent pipe");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Agent pipe connection failed");
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(pipe, stoppingToken), CancellationToken.None);
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            Product.ServicePipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            var processId = PipeIdentity.ClientProcessId(pipe) ?? -1;
            var sessionId = processId > 0 ? ProcessQuery.TryGetSessionId(processId) ?? -1 : -1;
            if (sessionId <= 0)
            {
                _logger.LogWarning("Rejected a pipe client without a user session (pid {Pid})", processId);
                return;
            }

            using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe, new SystemTextJsonFormatter()));
            var connection = new AgentConnection(sessionId, processId, rpc);
            rpc.AddLocalRpcTarget(new ServiceApi(connection, _hub, _supervisor, _activity, _machine, _updates));
            connection.Api = rpc.Attach<IAgentApi>();
            rpc.StartListening();

            try
            {
                await rpc.Completion.WaitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ConnectionLostException or IOException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Agent connection in session {Session} ended with an error", sessionId);
            }
            finally
            {
                _hub.Unregister(connection);
            }
        }
    }
}

/// <summary>The service methods an agent can call.</summary>
internal sealed class ServiceApi : IServiceApi
{
    private readonly AgentConnection _connection;
    private readonly AgentHub _hub;
    private readonly AgentSupervisor _supervisor;
    private readonly ActivityLog _activity;
    private readonly MachineHost _machine;
    private readonly UpdateService _updates;

    public ServiceApi(AgentConnection connection, AgentHub hub, AgentSupervisor supervisor, ActivityLog activity, MachineHost machine, UpdateService updates)
    {
        _updates = updates;
        _connection = connection;
        _hub = hub;
        _supervisor = supervisor;
        _activity = activity;
        _machine = machine;
    }

    public Task<ServiceHello> RegisterAgentAsync(AgentHello hello, CancellationToken cancellationToken)
    {
        _hub.Register(_connection);
        _supervisor.OnAgentConnected(_connection.SessionId, _connection.ProcessId);
        var reply = new ServiceHello(
            typeof(ServiceApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            _machine.ProcessMonitorName,
            Sessions.GetUser(_connection.SessionId));
        return Task.FromResult(reply);
    }

    public Task<IReadOnlyList<ActivityEntry>> GetMachineActivityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_activity.Snapshot());

    public Task AgentExitingAsync(CancellationToken cancellationToken)
    {
        _supervisor.OnUserExit(_connection.SessionId);
        return Task.CompletedTask;
    }

    public Task<UpdateStatus> GetUpdateStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_updates.Status());

    public Task<UpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
        _updates.CheckNowAsync(cancellationToken);

    public Task<string?> InstallUpdateAsync(CancellationToken cancellationToken)
    {
        var who = Sessions.GetUser(_connection.SessionId)?.QualifiedName ?? "session " + _connection.SessionId;
        _activity.Info(ActivitySources.Update, $"Install requested by {who}.");
        return _updates.InstallNowAsync(cancellationToken);
    }

    public Task<string?> SetUpdateSettingsAsync(UpdateSettings settings, CancellationToken cancellationToken) =>
        _updates.ChangeSettingsAsync(settings, cancellationToken);
}
