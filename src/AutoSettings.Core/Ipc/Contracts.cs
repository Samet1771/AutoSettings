using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Ipc;

/// <summary>Sent by an agent when it connects to the service.</summary>
/// <param name="SessionId">The agent's Windows session.</param>
/// <param name="ProcessId">The agent's process id.</param>
/// <param name="Version">Agent version.</param>
public sealed record AgentHello(int SessionId, int ProcessId, string Version);

/// <summary>The service's reply to <see cref="AgentHello"/>.</summary>
/// <param name="Version">Service version.</param>
/// <param name="ProcessMonitor">Name of the process monitor the service uses.</param>
/// <param name="User">The user of the agent's session, as seen by the service.</param>
public sealed record ServiceHello(string Version, string ProcessMonitor, UserInfo? User);

/// <summary>An action forwarded from a machine automation to a user's agent.</summary>
/// <param name="Type">Action type.</param>
/// <param name="ParametersJson">Parameters as a JSON object (see <see cref="PlainJson"/>).</param>
/// <param name="Event">The event that started the automation.</param>
/// <param name="AutomationId">Id of the machine automation.</param>
/// <param name="AutomationName">Display name of the machine automation.</param>
public sealed record RemoteAction(string Type, string ParametersJson, SystemEvent? Event, string? AutomationId, string? AutomationName);

/// <summary>Methods the service exposes to agents (agent → service).</summary>
public interface IServiceApi
{
    /// <summary>Registers the agent for its session. The service then sends it the session's events.</summary>
    Task<ServiceHello> RegisterAgentAsync(AgentHello hello, CancellationToken cancellationToken);

    /// <summary>Returns the machine activity log (for the agent's activity view).</summary>
    Task<IReadOnlyList<ActivityEntry>> GetMachineActivityAsync(CancellationToken cancellationToken);

    /// <summary>Tells the service that the user closed the agent on purpose, so it is not restarted until the next sign-in.</summary>
    Task AgentExitingAsync(CancellationToken cancellationToken);

    /// <summary>Returns the current update status.</summary>
    Task<UpdateStatus> GetUpdateStatusAsync(CancellationToken cancellationToken);

    /// <summary>Checks GitHub for a new version now and returns the resulting status.</summary>
    Task<UpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken);

    /// <summary>Downloads (if needed), verifies and installs the available update. Returns an error message, or null when the install started.</summary>
    Task<string?> InstallUpdateAsync(CancellationToken cancellationToken);

    /// <summary>Changes the update settings. Returns an error message (for example when an administrator locked them), or null.</summary>
    Task<string?> SetUpdateSettingsAsync(UpdateSettings settings, CancellationToken cancellationToken);
}

/// <summary>Methods agents expose to the service (service → agent).</summary>
public interface IAgentApi
{
    /// <summary>Delivers an event from the agent's session (sign-in, lock, app started/closed).</summary>
    Task OnSystemEventAsync(SystemEvent systemEvent, CancellationToken cancellationToken);

    /// <summary>Runs a user-scope action on behalf of a machine automation. Returns whether it succeeded.</summary>
    Task<bool> ExecuteActionAsync(RemoteAction action, CancellationToken cancellationToken);

    /// <summary>Captures the current value for a revertible action (machine profiles).</summary>
    Task<string?> CaptureActionAsync(RemoteAction action, CancellationToken cancellationToken);

    /// <summary>Restores a captured value (machine profiles).</summary>
    Task RestoreActionAsync(RemoteAction action, string? snapshot, CancellationToken cancellationToken);

    /// <summary>Tells the agent that the update status changed (new version found, downloaded, installing, updated).</summary>
    Task OnUpdateStatusChangedAsync(UpdateStatus status, CancellationToken cancellationToken);

    /// <summary>True while the user runs a full-screen app (game, presentation); automatic updates wait for it to end.</summary>
    Task<bool> IsBusyAsync(CancellationToken cancellationToken);
}
