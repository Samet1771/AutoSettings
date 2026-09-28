using System.Collections.Concurrent;
using System.Diagnostics;
using AutoSettings.Core;
using AutoSettings.Core.Engine;
using AutoSettings.Platform.Monitoring;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoSettings.Service;

/// <summary>
/// Starts the agent in every user session and restarts it if it crashes. An agent the user closed
/// on purpose stays closed until the next sign-in.
/// </summary>
public sealed class AgentSupervisor
{
    private const int MaxRestarts = 5;
    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(10);

    private sealed class SessionState
    {
        public bool UserExited { get; set; }
        public List<DateTimeOffset> Restarts { get; } = [];
        public HashSet<int> WatchedProcesses { get; } = [];
    }

    private readonly ConcurrentDictionary<int, SessionState> _sessions = new();
    private readonly AgentHub _hub;
    private readonly ActivityLog _activity;
    private readonly ServiceOptions _options;
    private readonly ILogger<AgentSupervisor> _logger;
    private readonly bool _enabled;

    public AgentSupervisor(AgentHub hub, ActivityLog activity, IOptions<ServiceOptions> options, ILogger<AgentSupervisor> logger)
    {
        _hub = hub;
        _activity = activity;
        _options = options.Value;
        _logger = logger;
        // Launching into user sessions only works when running as SYSTEM, i.e. as the installed service.
        _enabled = _options.LaunchAgents && WindowsServiceHelpers.IsWindowsService();
    }

    /// <summary>Starts agents for users who were already signed in when the service started.</summary>
    public void StartForExistingSessions()
    {
        foreach (var session in Sessions.List())
            Ensure(session.SessionId);
    }

    public void OnLogon(int sessionId)
    {
        _sessions[sessionId] = new SessionState();
        Ensure(sessionId);
    }

    public void OnLogoff(int sessionId) => _sessions.TryRemove(sessionId, out _);

    public void OnUserExit(int sessionId)
    {
        _sessions.GetOrAdd(sessionId, _ => new SessionState()).UserExited = true;
        _logger.LogInformation("The user closed the agent in session {Session}; it will start again at the next sign-in", sessionId);
    }

    public void OnAgentConnected(int sessionId, int processId)
    {
        var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());
        state.UserExited = false;
        Watch(sessionId, processId);
    }

    private void Ensure(int sessionId)
    {
        if (!_enabled)
            return;
        var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());
        if (state.UserExited || _hub.IsConnected(sessionId) || IsAgentRunning(sessionId))
            return;

        var path = _options.ResolvedAgentPath;
        if (!File.Exists(path))
        {
            _logger.LogError("Agent not found at {Path}; personal automations will not run", path);
            return;
        }

        try
        {
            var processId = UserProcessLauncher.Launch(sessionId, path, "--background");
            _logger.LogInformation("Started agent in session {Session} (pid {Pid})", sessionId, processId);
            Watch(sessionId, processId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start the agent in session {Session}", sessionId);
        }
    }

    private void Watch(int sessionId, int processId)
    {
        if (!_enabled)
            return;
        var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());
        lock (state)
        {
            if (!state.WatchedProcesses.Add(processId))
                return;
        }
        try
        {
            var process = Process.GetProcessById(processId);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                var exitCode = SafeExitCode(process);
                process.Dispose();
                OnAgentExited(sessionId, exitCode);
            };
        }
        catch (ArgumentException)
        {
            OnAgentExited(sessionId, -1);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot watch agent process {Pid}", processId);
        }
    }

    private void OnAgentExited(int sessionId, int exitCode)
    {
        if (!_sessions.TryGetValue(sessionId, out var state) || state.UserExited || exitCode == 0)
            return;

        var now = DateTimeOffset.Now;
        lock (state)
        {
            state.Restarts.RemoveAll(t => now - t > RestartWindow);
            if (state.Restarts.Count >= MaxRestarts)
            {
                _activity.Error(ActivitySources.Connection,
                    $"The agent in session {sessionId} keeps stopping (exit code {exitCode}); it will not be restarted until the next sign-in. See {Product.AgentLogDirectory}.");
                return;
            }
            state.Restarts.Add(now);
        }

        var delay = TimeSpan.FromSeconds(5 * Math.Pow(2, state.Restarts.Count - 1));
        _logger.LogWarning("Agent in session {Session} exited with code {Code}; restarting in {Delay}", sessionId, exitCode, delay);
        _ = Task.Delay(delay).ContinueWith(_ => Ensure(sessionId), TaskScheduler.Default);
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static bool IsAgentRunning(int sessionId)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Product.AgentExecutableName));
        try
        {
            return processes.Any(p => p.SessionId == sessionId);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
