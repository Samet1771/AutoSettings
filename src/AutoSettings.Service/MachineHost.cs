using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using AutoSettings.Core;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Platform;
using AutoSettings.Platform.Monitoring;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoSettings.Service;

/// <summary>
/// Runs machine automations and turns Windows notifications (boot, sessions, processes) into events
/// for the machine engine and for the agents.
/// </summary>
public sealed class MachineHost : BackgroundService
{
    private readonly SystemSignals _signals;
    private readonly AgentHub _hub;
    private readonly AgentSupervisor _supervisor;
    private readonly ActivityLog _activity;
    private readonly ServiceOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MachineHost> _logger;
    private readonly ConcurrentDictionary<int, UserInfo?> _sessionUsers = new();
    private RuleEngine? _engine;
    private IProcessMonitor? _processMonitor;

    public MachineHost(
        SystemSignals signals,
        AgentHub hub,
        AgentSupervisor supervisor,
        ActivityLog activity,
        IOptions<ServiceOptions> options,
        ILoggerFactory loggerFactory)
    {
        _signals = signals;
        _hub = hub;
        _supervisor = supervisor;
        _activity = activity;
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MachineHost>();
    }

    /// <summary>Name of the process monitor in use.</summary>
    public string ProcessMonitorName => _processMonitor?.Name ?? "starting";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting before doing real work.
        await Task.Yield();

        var runningAsService = WindowsServiceHelpers.IsWindowsService();
        if (runningAsService)
            SecureDataDirectory(Product.MachineDataDirectory);

        var catalog = ComponentCatalog.Default;
        var engine = new RuleEngine(
            ExecutionScope.Machine,
            BuildHandlers(catalog),
            _activity,
            new EngineOptions { MaxRunsPerMinute = _options.MaxRunsPerMinute, LogAllEvents = _options.LogAllEvents },
            catalog,
            logger: _loggerFactory.CreateLogger<RuleEngine>());
        _engine = engine;

        using var store = new ConfigFileStore(Product.MachineConfigPath, ExecutionScope.Machine, catalog, _loggerFactory.CreateLogger<ConfigFileStore>());
        store.Loaded += (_, result) => OnConfigLoaded(engine, result);
        try
        {
            store.EnsureExists(StarterConfig.Machine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Cannot create {Path}", store.FilePath);
        }
        store.Load();
        store.StartWatching();

        try
        {
            _processMonitor = ProcessMonitorFactory.CreateAndStart(_logger);
            engine.Processes.Seed(_processMonitor.RunningProcesses);
            _processMonitor.EventRaised += (_, e) => OnProcessEvent(engine, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App start/close detection is not available");
            _activity.Error(ActivitySources.Engine, "App start/close detection is not available: " + ex.Message);
        }

        _signals.SessionChanged += (reason, sessionId) => OnSessionChanged(engine, reason, sessionId);
        _signals.PowerChanged += status => OnPowerChanged(engine, status);

        foreach (var session in Sessions.List())
            _sessionUsers[session.SessionId] = session.User;

        if (new BootDetector().IsFirstStartSinceBoot())
            engine.Post(new SystemEvent { Kind = SystemEventKind.Boot, BootType = BootTypes.Cold });

        _supervisor.StartForExistingSessions();
        _activity.Info(ActivitySources.Engine, $"{Product.Name} service started ({(runningAsService ? "Windows Service" : "console")}, app detection: {ProcessMonitorName}).");

        try
        {
            await engine.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            _processMonitor?.Dispose();
        }
    }

    private HandlerRegistry BuildHandlers(ComponentCatalog catalog)
    {
        var registry = new HandlerRegistry();
        var local = PlatformHandlers.MachineActions().ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in catalog.OfKind(ComponentKind.Action))
        {
            if (descriptor.Type is BuiltInActions.ProfileApply or BuiltInActions.ProfileRevert or BuiltInActions.Delay)
                continue;
            registry.Add(new RoutingActionHandler(descriptor, local.GetValueOrDefault(descriptor.Type), _hub));
        }
        foreach (var condition in PlatformHandlers.CommonConditions())
            registry.Add(condition);
        return registry;
    }

    private void OnConfigLoaded(RuleEngine engine, ConfigLoadResult result)
    {
        if (result.HasErrors)
        {
            _activity.Error(ActivitySources.Config,
                $"{Product.MachineConfigPath} has {result.Errors.Count()} error(s); the previous machine automations keep running. First: {result.Errors.First().Message}");
            return;
        }
        engine.UpdateConfig(result.Config);
        _activity.Info(ActivitySources.Config,
            $"Loaded {result.Config.Automations.Count} machine automation(s) and {result.Config.Profiles.Count} profile(s).");
    }

    private void OnProcessEvent(RuleEngine engine, SystemEvent e)
    {
        if (e.SessionId is { } sessionId && sessionId > 0)
            e = e with { User = UserFor(sessionId) };
        engine.Post(e);
        _hub.SendEvent(e);
    }

    private void OnSessionChanged(RuleEngine engine, SessionChangeReason reason, int sessionId)
    {
        SystemEventKind? kind = reason switch
        {
            SessionChangeReason.SessionLogon => SystemEventKind.Logon,
            SessionChangeReason.SessionLogoff => SystemEventKind.Logoff,
            SessionChangeReason.SessionLock => SystemEventKind.Lock,
            SessionChangeReason.SessionUnlock => SystemEventKind.Unlock,
            _ => null,
        };
        if (kind is null)
            return;

        if (kind == SystemEventKind.Logon)
            _sessionUsers[sessionId] = Sessions.GetUser(sessionId);

        var e = new SystemEvent
        {
            Kind = kind.Value,
            SessionId = sessionId,
            User = UserFor(sessionId),
            IsRemote = Sessions.IsRemote(sessionId),
        };
        engine.Post(e);

        switch (kind)
        {
            case SystemEventKind.Logon:
                _hub.SendLogon(e);
                _supervisor.OnLogon(sessionId);
                break;
            case SystemEventKind.Logoff:
                _hub.SendEvent(e);
                _hub.ForgetSession(sessionId);
                _supervisor.OnLogoff(sessionId);
                _sessionUsers.TryRemove(sessionId, out _);
                break;
            default:
                _hub.SendEvent(e);
                break;
        }
    }

    private void OnPowerChanged(RuleEngine engine, PowerBroadcastStatus status)
    {
        if (status != PowerBroadcastStatus.ResumeAutomatic)
            return;
        // With Fast Startup, "shutting down" hibernates the kernel and the service; the next start is a resume.
        if (BootInfo.RecentBootType(TimeSpan.FromMinutes(10)) == BootTypes.FastStartup)
            engine.Post(new SystemEvent { Kind = SystemEventKind.Boot, BootType = BootTypes.FastStartup });
    }

    private UserInfo? UserFor(int sessionId) => _sessionUsers.GetOrAdd(sessionId, Sessions.GetUser);

    /// <summary>
    /// Only administrators and SYSTEM may change machine automations (they can run commands as SYSTEM);
    /// users may read them and the logs.
    /// </summary>
    private void SecureDataDirectory(string path)
    {
        try
        {
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

            var directory = new DirectoryInfo(path);
            if (directory.Exists)
                directory.SetAccessControl(security);
            else
                directory.Create(security);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not secure {Path}", path);
        }
    }
}
