using AutoSettings.Core;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Updates;
using AutoSettings.Platform;
using AutoSettings.Platform.Actions;
using AutoSettings.Platform.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace AutoSettings.Agent;

/// <summary>
/// Everything the agent runs: the personal automation engine, the focus monitor, the configuration
/// file and the connection to the service.
/// </summary>
public sealed class AgentHost
{
    private readonly INotifier _notifier;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _fallbackGate = new();
    private IProcessMonitor? _fallback;
    private Task? _engineTask;

    public AgentHost(INotifier notifier, ILoggerFactory loggerFactory)
    {
        _notifier = notifier;
        _logger = loggerFactory.CreateLogger<AgentHost>();
        User = Sessions.CurrentUser();
        SessionId = Sessions.CurrentSessionId();
        Activity = new ActivityLog(1000, loggerFactory.CreateLogger("Activity"));
        Foreground = new ForegroundMonitor();
        Handlers = PlatformHandlers.CreateUserRegistry(notifier, Foreground);
        Engine = new RuleEngine(ExecutionScope.User, Handlers, Activity, logger: loggerFactory.CreateLogger<RuleEngine>())
        {
            CurrentUser = User,
        };
        Store = new ConfigFileStore(Product.UserConfigPath, ExecutionScope.User, ComponentCatalog.Default, loggerFactory.CreateLogger<ConfigFileStore>());
        Service = new ServiceConnection(new AgentApi(this), Activity, SessionId, loggerFactory.CreateLogger<ServiceConnection>());
    }

    /// <summary>The signed-in user.</summary>
    public UserInfo User { get; }

    /// <summary>This agent's Windows session.</summary>
    public int SessionId { get; }

    public ActivityLog Activity { get; }

    public ForegroundMonitor Foreground { get; }

    public HandlerRegistry Handlers { get; }

    public RuleEngine Engine { get; }

    public ConfigFileStore Store { get; }

    public ServiceConnection Service { get; }

    /// <summary>How app starts/exits are detected right now.</summary>
    public string AppDetection
    {
        get
        {
            if (Service.IsConnected)
                return $"by the {Product.Name} service ({Service.ProcessMonitorName})";
            lock (_fallbackGate)
                return _fallback is not null ? $"by the agent ({_fallback.Name})" : "not active";
        }
    }

    /// <summary>Raised when the connection, configuration or engine state changes. May be raised on any thread.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>The last update status from the service, or null when not known.</summary>
    public UpdateStatus? Updates { get; private set; }

    /// <summary>Raised when the update status changes. May be raised on any thread.</summary>
    public event EventHandler<UpdateStatus>? UpdateStatusChanged;

    internal void SetUpdateStatus(UpdateStatus status)
    {
        Updates = status;
        UpdateStatusChanged?.Invoke(this, status);
    }

    /// <summary>Checks for updates now.</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (await Service.CheckForUpdatesAsync().ConfigureAwait(false) is { } status)
            SetUpdateStatus(status);
    }

    /// <summary>Installs the available update. Returns an error message, or null when the install started.</summary>
    public Task<string?> InstallUpdateAsync() => Service.InstallUpdateAsync();

    /// <summary>Changes the update settings. Returns an error message or null.</summary>
    public async Task<string?> SetUpdateSettingsAsync(UpdateSettings settings)
    {
        var error = await Service.SetUpdateSettingsAsync(settings).ConfigureAwait(false);
        if (await Service.GetUpdateStatusAsync().ConfigureAwait(false) is { } status)
            SetUpdateStatus(status);
        return error;
    }

    /// <summary>Starts everything. Must be called on the UI thread (the focus hook needs its message loop).</summary>
    public async Task StartAsync()
    {
        Foreground.EventRaised += (_, e) => Engine.Post(e);
        Foreground.Start();

        Engine.Processes.Seed(ProcessQuery.Snapshot(SessionId));
        Engine.StateChanged += (_, _) => RaiseStatusChanged();

        Store.Loaded += OnConfigLoaded;
        Store.EnsureExists(StarterConfig.User);
        Store.Load();
        Store.StartWatching();

        SystemEvents.SessionSwitch += OnSessionSwitch;

        _engineTask = Task.Run(() => Engine.RunAsync(_stopping.Token));

        Service.ConnectionChanged += connected =>
        {
            if (connected)
            {
                StopFallback();
                _ = Task.Run(async () =>
                {
                    if (await Service.GetUpdateStatusAsync().ConfigureAwait(false) is { } status)
                        SetUpdateStatus(status);
                });
            }
            else
                StartFallback();
            RaiseStatusChanged();
        };
        Service.Start();

        // If the service does not answer quickly, detect apps ourselves until it does.
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (!Service.IsConnected)
            StartFallback();
    }

    /// <summary>Stops everything. When the user closed the agent, the service is told not to restart it.</summary>
    public async Task StopAsync(bool userInitiated)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (userInitiated)
            await Service.NotifyExitingAsync().ConfigureAwait(false);

        try
        {
            await Engine.RevertAllProfilesAsync($"{Product.Name} closed").WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not revert active profiles on exit");
        }

        _stopping.Cancel();
        await Service.DisposeAsync().ConfigureAwait(false);
        StopFallback();
        Foreground.Dispose();
        Store.Dispose();
        if (_engineTask is not null)
            await _engineTask.ConfigureAwait(false);
    }

    public Task ApplyProfileAsync(string profileId) => RunSafely(() => Engine.ApplyProfileAsync(profileId));

    public Task RevertProfileAsync(string profileId) => RunSafely(() => Engine.RevertProfileAsync(profileId));

    public Task RunAutomationAsync(string automationId, bool checkConditions) =>
        RunSafely(() => Engine.RunAutomationAsync(automationId, checkConditions));

    /// <summary>Runs the actions of an automation that may not be saved yet (the editor's Test button).</summary>
    public Task TestActionsAsync(Core.Model.Automation automation) => RunSafely(async () =>
    {
        var context = new ActionContext(null, automation.Id, automation.DisplayName, Activity) { CurrentUser = User };
        Activity.Info(ActivitySources.Automation, $"Testing '{automation.DisplayName}'.", automation.Id);
        foreach (var action in automation.Actions)
        {
            if (!await Engine.ExecuteActionAsync(action, context, CancellationToken.None).ConfigureAwait(false)
                && action.GetBoolean("continue_on_error") != true)
                break;
        }
    });

    /// <summary>
    /// Changes the configuration through the editor model and saves it. Refuses while the file has errors,
    /// because the editor works on the last valid version and would overwrite the user's unfinished edit.
    /// </summary>
    public ConfigLoadResult Edit(Action<Core.Editing.ConfigDocument> change)
    {
        if (Store.LastResult?.HasErrors == true)
            throw new InvalidOperationException(Localization.Strings.Get("FixFileFirst"));
        var document = new Core.Editing.ConfigDocument(Store.Current, ExecutionScope.User);
        change(document);
        return Store.Save(document.ToYaml());
    }

    /// <summary>A copy of the current configuration for editing.</summary>
    public Core.Editing.ConfigDocument BeginEdit()
    {
        if (Store.LastResult?.HasErrors == true)
            throw new InvalidOperationException(Localization.Strings.Get("FixFileFirst"));
        return new Core.Editing.ConfigDocument(Store.Current, ExecutionScope.User);
    }

    private async Task RunSafely(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual action failed");
            Activity.Error(ActivitySources.Engine, ex.Message);
        }
    }

    private void OnConfigLoaded(object? sender, ConfigLoadResult result)
    {
        if (result.HasErrors)
        {
            var first = result.Errors.First();
            Activity.Error(ActivitySources.Config,
                $"automations.yaml has {result.Errors.Count()} error(s); the previous automations keep running. {first}");
            _notifier.Notify(Localization.Strings.Format("FileErrorsTitle", Product.Name), first.ToString());
        }
        else
        {
            Engine.UpdateConfig(result.Config);
            var warnings = result.Warnings.Count();
            Activity.Info(ActivitySources.Config,
                $"Loaded {result.Config.Automations.Count} automation(s) and {result.Config.Profiles.Count} profile(s){(warnings > 0 ? $" with {warnings} warning(s)" : "")}.");
        }
        RaiseStatusChanged();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        // The service reports lock/unlock when it is connected; this is the fallback without it.
        if (Service.IsConnected)
            return;
        SystemEventKind? kind = e.Reason switch
        {
            SessionSwitchReason.SessionLock => SystemEventKind.Lock,
            SessionSwitchReason.SessionUnlock => SystemEventKind.Unlock,
            _ => null,
        };
        if (kind is { } k)
            Engine.Post(new SystemEvent { Kind = k, SessionId = SessionId, User = User });
    }

    private void StartFallback()
    {
        lock (_fallbackGate)
        {
            if (_fallback is not null || _stopping.IsCancellationRequested)
                return;
            var monitor = new PollingProcessMonitor(SessionId);
            monitor.EventRaised += (_, e) => Engine.Post(e);
            monitor.Start();
            _fallback = monitor;
        }
        Activity.Warning(ActivitySources.Connection,
            $"The {Product.Name} service is not reachable. App start/close is detected by checking the app list every 2 seconds, and sign-in automations will not run.");
        RaiseStatusChanged();
    }

    private void StopFallback()
    {
        lock (_fallbackGate)
        {
            _fallback?.Dispose();
            _fallback = null;
        }
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);
}
