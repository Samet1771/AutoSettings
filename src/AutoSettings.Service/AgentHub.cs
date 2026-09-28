using System.Collections.Concurrent;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Ipc;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace AutoSettings.Service;

/// <summary>A connected agent.</summary>
public sealed class AgentConnection
{
    public AgentConnection(int sessionId, int processId, JsonRpc rpc)
    {
        SessionId = sessionId;
        ProcessId = processId;
        Rpc = rpc;
    }

    /// <summary>The agent's session, taken from the pipe client process (not from what the agent claims).</summary>
    public int SessionId { get; }

    public int ProcessId { get; }

    public JsonRpc Rpc { get; }

    public IAgentApi? Api { get; set; }
}

/// <summary>Keeps track of connected agents and delivers events and actions to them.</summary>
public sealed class AgentHub
{
    private static readonly TimeSpan PendingLogonLifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<int, AgentConnection> _agents = new();
    private readonly ConcurrentDictionary<int, SystemEvent> _pendingLogons = new();
    private readonly ActivityLog _activity;
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(ActivityLog activity, ILogger<AgentHub> logger)
    {
        _activity = activity;
        _logger = logger;
    }

    /// <summary>Raised when an agent connects or disconnects.</summary>
    public event Action<int, bool>? AgentConnectionChanged;

    public bool IsConnected(int sessionId) => _agents.ContainsKey(sessionId);

    /// <summary>The agent API for a session, or null when no agent is connected there.</summary>
    public IAgentApi? For(int? sessionId) =>
        sessionId is { } id && _agents.TryGetValue(id, out var connection) ? connection.Api : null;

    public void Register(AgentConnection connection)
    {
        _agents[connection.SessionId] = connection;
        _logger.LogInformation("Agent connected in session {Session} (pid {Pid})", connection.SessionId, connection.ProcessId);
        _activity.Info(ActivitySources.Connection, $"Agent connected in session {connection.SessionId}.");
        AgentConnectionChanged?.Invoke(connection.SessionId, true);

        if (_pendingLogons.TryRemove(connection.SessionId, out var logon) && DateTimeOffset.Now - logon.Timestamp < PendingLogonLifetime)
            Send(connection, logon);
    }

    public void Unregister(AgentConnection connection)
    {
        if (_agents.TryRemove(new KeyValuePair<int, AgentConnection>(connection.SessionId, connection)))
        {
            _logger.LogInformation("Agent in session {Session} disconnected", connection.SessionId);
            _activity.Info(ActivitySources.Connection, $"Agent in session {connection.SessionId} disconnected.");
            AgentConnectionChanged?.Invoke(connection.SessionId, false);
        }
    }

    /// <summary>Sends an event to the agent of its session, if one is connected.</summary>
    public void SendEvent(SystemEvent e)
    {
        if (e.SessionId is { } id && _agents.TryGetValue(id, out var connection))
            Send(connection, e);
    }

    /// <summary>
    /// Delivers a logon event to the session's agent. Agents usually start right after logon, so the
    /// event is kept until the agent connects.
    /// </summary>
    public void SendLogon(SystemEvent e)
    {
        if (e.SessionId is not { } id)
            return;
        if (_agents.TryGetValue(id, out var connection))
            Send(connection, e);
        else
            _pendingLogons[id] = e;
    }

    public void ForgetSession(int sessionId) => _pendingLogons.TryRemove(sessionId, out _);

    /// <summary>The APIs of all connected agents.</summary>
    public IReadOnlyList<IAgentApi> All() =>
        _agents.Values.Select(c => c.Api).OfType<IAgentApi>().ToList();

    /// <summary>Calls every connected agent in the background, ignoring agents that fail (for example older versions).</summary>
    public void Broadcast(Func<IAgentApi, Task> call, string what)
    {
        foreach (var connection in _agents.Values)
        {
            if (connection.Api is not { } api)
                continue;
            _ = Task.Run(async () =>
            {
                try
                {
                    await call(api).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not deliver {What} to session {Session}", what, connection.SessionId);
                }
            });
        }
    }

    private void Send(AgentConnection connection, SystemEvent e)
    {
        if (connection.Api is not { } api)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await api.OnSystemEventAsync(e, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not deliver {Event} to session {Session}", e.Kind, connection.SessionId);
            }
        });
    }
}
