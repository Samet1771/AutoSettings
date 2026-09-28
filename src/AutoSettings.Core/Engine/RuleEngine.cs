using System.Threading.Channels;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoSettings.Core.Engine;

/// <summary>Tunables for <see cref="RuleEngine"/>.</summary>
public sealed class EngineOptions
{
    /// <summary>
    /// Loop guard: an automation that runs more often than this within one minute is suspended
    /// (for example two automations that keep undoing each other).
    /// </summary>
    public int MaxRunsPerMinute { get; set; } = 20;

    /// <summary>Write every received event to the activity log (verbose; for troubleshooting).</summary>
    public bool LogAllEvents { get; set; }
}

/// <summary>
/// Runs automations: matches events against triggers, evaluates conditions and executes actions.
/// One engine runs in the service (machine automations) and one in each user's agent (personal automations).
/// </summary>
/// <remarks>
/// Events are processed one at a time in the order they arrive. An automation's actions run in the
/// background so a long <c>delay</c> does not hold up other automations. An automation that is still
/// running ignores new triggers until it finishes.
/// </remarks>
public sealed class RuleEngine
{
    private sealed record CompiledAutomation(Automation Automation, IReadOnlyList<(ComponentDescriptor Descriptor, ComponentConfig Trigger)> Triggers);

    private sealed record State(AutomationConfig Config, IReadOnlyList<CompiledAutomation> Automations);

    private readonly HandlerRegistry _handlers;
    private readonly EngineOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ProfileManager _profiles;
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly Channel<SystemEvent> _queue = Channel.CreateUnbounded<SystemEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _runGate = new();
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suspended = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastRun = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _recentRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _inFlight = [];
    private volatile State _state;
    private DateTimeOffset? _pausedUntil;
    private bool _pausedIndefinitely;

    /// <summary>Creates an engine.</summary>
    /// <param name="scope">Whether this engine runs personal (agent) or machine (service) automations.</param>
    /// <param name="handlers">Action and condition handlers available where the engine runs.</param>
    /// <param name="log">Activity log to write to.</param>
    /// <param name="options">Tunables.</param>
    /// <param name="catalog">Component catalog (built-in by default).</param>
    /// <param name="time">Clock (for tests).</param>
    /// <param name="logger">Diagnostic logger.</param>
    public RuleEngine(
        ExecutionScope scope,
        HandlerRegistry handlers,
        ActivityLog log,
        EngineOptions? options = null,
        ComponentCatalog? catalog = null,
        TimeProvider? time = null,
        ILogger? logger = null)
    {
        Scope = scope;
        _handlers = handlers;
        Log = log;
        _options = options ?? new EngineOptions();
        Catalog = catalog ?? ComponentCatalog.Default;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _profiles = new ProfileManager(Catalog, handlers, log, _time, ExecuteActionAsync);
        _profiles.Changed += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _state = new State(new AutomationConfig(), []);
    }

    /// <summary>Personal (user) or machine engine.</summary>
    public ExecutionScope Scope { get; }

    /// <summary>The activity log.</summary>
    public ActivityLog Log { get; }

    /// <summary>The component catalog.</summary>
    public ComponentCatalog Catalog { get; }

    /// <summary>Known running processes (seed it at startup).</summary>
    public ProcessTracker Processes { get; } = new();

    /// <summary>The signed-in user this engine runs for (agents). Events without a user are attributed to them.</summary>
    public UserInfo? CurrentUser { get; set; }

    /// <summary>When true, actions are written to the activity log instead of being executed.</summary>
    public bool DryRun { get; set; }

    /// <summary>The configuration currently running.</summary>
    public AutomationConfig Config => _state.Config;

    /// <summary>Raised when pause state, suspended automations or active profiles change. May be raised on any thread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Whether automations are paused (profiles still revert automatically while paused).</summary>
    public bool IsPaused
    {
        get
        {
            lock (_runGate)
            {
                if (_pausedIndefinitely)
                    return true;
                return _pausedUntil is { } until && _time.GetLocalNow() < until;
            }
        }
    }

    /// <summary>When a timed pause ends, or null.</summary>
    public DateTimeOffset? PausedUntil
    {
        get
        {
            lock (_runGate)
                return _pausedUntil;
        }
    }

    /// <summary>Automations suspended by the loop guard.</summary>
    public IReadOnlyCollection<string> SuspendedAutomations
    {
        get
        {
            lock (_runGate)
                return _suspended.ToList();
        }
    }

    /// <summary>Currently applied profiles, highest priority first.</summary>
    public IReadOnlyList<ActiveProfileInfo> ActiveProfiles => _profiles.Active;

    /// <summary>Replaces the running configuration. Clears loop-guard suspensions.</summary>
    public void UpdateConfig(AutomationConfig config)
    {
        var compiled = new List<CompiledAutomation>();
        foreach (var automation in config.Automations)
        {
            var triggers = new List<(ComponentDescriptor, ComponentConfig)>();
            foreach (var trigger in automation.Triggers)
            {
                var descriptor = Catalog.Find(ComponentKind.Trigger, trigger.Type);
                if (descriptor?.EventKind is null)
                    continue;
                triggers.Add((descriptor, Catalog.WithDefaults(ComponentKind.Trigger, trigger)));
            }
            compiled.Add(new CompiledAutomation(automation, triggers));
        }
        _state = new State(config, compiled);
        lock (_runGate)
            _suspended.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pauses automations for <paramref name="duration"/>, or until <see cref="Resume"/> when null.</summary>
    public void Pause(TimeSpan? duration = null)
    {
        lock (_runGate)
        {
            _pausedIndefinitely = duration is null;
            _pausedUntil = duration is { } d ? _time.GetLocalNow() + d : null;
        }
        Log.Info(ActivitySources.Engine, duration is { } span
            ? $"Automations paused for {ValueConverter.FormatDuration(span)}."
            : "Automations paused until resumed.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resumes after <see cref="Pause"/>.</summary>
    public void Resume()
    {
        lock (_runGate)
        {
            _pausedIndefinitely = false;
            _pausedUntil = null;
        }
        Log.Info(ActivitySources.Engine, "Automations resumed.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lifts a loop-guard suspension.</summary>
    public void ResumeAutomation(string automationId)
    {
        bool removed;
        lock (_runGate)
        {
            removed = _suspended.Remove(automationId);
            _recentRuns.Remove(automationId);
        }
        if (removed)
        {
            Log.Info(ActivitySources.Engine, $"Automation '{automationId}' resumed.", automationId);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Queues an event for processing by <see cref="RunAsync"/>. Thread-safe.</summary>
    public void Post(SystemEvent e) => _queue.Writer.TryWrite(e);

    /// <summary>Processes queued events until <paramref name="cancellationToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var e in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await HandleEventAsync(e, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while handling {Event}", e);
                    Log.Error(ActivitySources.Engine, $"Internal error while handling '{EventNames.Describe(e)}': {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Processes one event: reverts profiles whose revert rule matches, then starts every enabled
    /// automation whose trigger matches and whose conditions are true. Returns once the actions have
    /// been started (see <see cref="WhenIdleAsync"/>).
    /// </summary>
    public async Task HandleEventAsync(SystemEvent e, CancellationToken cancellationToken = default)
    {
        if (e.User is null && CurrentUser is not null)
            e = e with { User = CurrentUser };

        await _eventGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var facts = Processes.Observe(e);
            var isAppEvent = e.Process is not null;
            if (_options.LogAllEvents || !isAppEvent)
                Log.Info(ActivitySources.Event, Capitalize(EventNames.Describe(e)) + ".");
            else
                _logger.LogDebug("Event {Event}", e);

            var eventContext = CreateActionContext(e, null, null);
            await _profiles.ProcessEventAsync(e, facts, eventContext, cancellationToken).ConfigureAwait(false);

            if (IsPaused)
                return;

            foreach (var compiled in _state.Automations)
            {
                var automation = compiled.Automation;
                if (!automation.Enabled)
                    continue;

                if (!compiled.Triggers.Any(t => TriggerMatcher.Matches(t.Descriptor, t.Trigger, e, facts)))
                    continue;

                if (!TryReserveRun(automation, e))
                    continue;

                var conditionContext = new ConditionContext(e, CurrentUser, Processes, _time);
                var (ok, failed) = await EvaluateAllAsync(automation.Conditions, conditionContext, cancellationToken).ConfigureAwait(false);
                if (!ok)
                {
                    ReleaseRun(automation.Id);
                    Log.Info(ActivitySources.Automation,
                        $"'{automation.DisplayName}' was triggered ({EventNames.Describe(e)}) but {failed} was not met.", automation.Id);
                    continue;
                }

                StartRun(automation, e, cancellationToken);
            }
        }
        finally
        {
            _eventGate.Release();
        }
    }

    /// <summary>Runs an automation now (the editor's Test button), even if it is disabled or automations are paused.</summary>
    /// <returns>False when the automation does not exist or its conditions were not met.</returns>
    public async Task<bool> RunAutomationAsync(string automationId, bool checkConditions, CancellationToken cancellationToken = default)
    {
        var automation = _state.Config.FindAutomation(automationId);
        if (automation is null)
            return false;

        if (checkConditions)
        {
            var (ok, failed) = await EvaluateAllAsync(automation.Conditions, new ConditionContext(null, CurrentUser, Processes, _time), cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                Log.Info(ActivitySources.Automation, $"'{automation.DisplayName}' not run: {failed} was not met.", automation.Id);
                return false;
            }
        }

        Log.Info(ActivitySources.Automation, $"Running '{automation.DisplayName}' manually.", automation.Id);
        await RunActionsAsync(automation, null, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Applies a profile manually. It stays active until reverted.</summary>
    public async Task<bool> ApplyProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var profile = _state.Config.FindProfile(profileId);
        if (profile is null)
            return false;
        await _profiles.ApplyAsync(profile, CreateActionContext(null, null, "manual"), null, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Reverts a profile manually.</summary>
    public Task<bool> RevertProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
        _profiles.RevertAsync(profileId, CreateActionContext(null, null, "manual"), "reverted manually", cancellationToken);

    /// <summary>Reverts every active profile (for example when the agent exits).</summary>
    public Task RevertAllProfilesAsync(string reason, CancellationToken cancellationToken = default) =>
        _profiles.RevertAllAsync(CreateActionContext(null, null, reason), reason, cancellationToken);

    /// <summary>Whether a profile is active.</summary>
    public bool IsProfileActive(string profileId) => _profiles.IsActive(profileId);

    /// <summary>Waits until all running automations have finished (for tests and shutdown).</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_runGate)
                pending = _inFlight.Where(t => !t.IsCompleted).ToArray();
            if (pending.Length == 0)
                return;
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes a single action with logging, placeholders and error handling.
    /// Used for automation actions, profile actions and actions forwarded from the service.
    /// </summary>
    /// <returns>Whether the action succeeded.</returns>
    public async Task<bool> ExecuteActionAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        var descriptor = Catalog.Find(ComponentKind.Action, action.Type);
        var effective = Catalog.WithDefaults(ComponentKind.Action, action);
        try
        {
            switch (action.Type)
            {
                case BuiltInActions.ProfileApply:
                {
                    var id = effective.GetString("profile") ?? "";
                    var profile = _state.Config.FindProfile(id) ?? throw new ActionFailedException($"there is no profile '{id}'");
                    var revert = RevertRule.Create(effective.GetString("revert_on") ?? "auto", context.Event);
                    await _profiles.ApplyAsync(profile, context, revert, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                case BuiltInActions.ProfileRevert:
                {
                    var id = effective.GetString("profile") ?? "";
                    if (!await _profiles.RevertAsync(id, context, $"requested by '{context.AutomationName}'", cancellationToken).ConfigureAwait(false))
                        Log.Info(ActivitySources.Profile, $"Profile '{id}' was not active.", context.AutomationId);
                    return true;
                }
                case BuiltInActions.Delay:
                {
                    var duration = effective.GetDuration("duration") ?? TimeSpan.Zero;
                    if (context.DryRun)
                        Log.Info(ActivitySources.Action, $"[dry run] Would wait {ValueConverter.FormatDuration(duration)}.", context.AutomationId);
                    else
                        await Task.Delay(duration, _time, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            var handler = _handlers.FindAction(action.Type)
                ?? throw new ActionFailedException($"'{action.Type}' is not available in {(Scope == ExecutionScope.User ? "personal" : "machine")} automations on this computer");

            var expanded = Placeholders.Expand(effective, descriptor, Placeholders.ValuesFor(context, _time.GetLocalNow()));
            if (context.DryRun)
            {
                Log.Info(ActivitySources.Action, $"[dry run] Would run {Summarize(action)}.", context.AutomationId);
                return true;
            }

            await handler.ExecuteAsync(expanded, context, cancellationToken).ConfigureAwait(false);
            Log.Success(ActivitySources.Action, $"{Summarize(action)} done.", context.AutomationId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Action {Action} failed", action.Type);
            Log.Error(ActivitySources.Action, $"{Summarize(action)} failed: {ex.Message}", context.AutomationId);
            return false;
        }
    }

    private bool TryReserveRun(Automation automation, SystemEvent e)
    {
        var now = _time.GetLocalNow();
        string? skipped = null;
        var suspendedNow = false;

        lock (_runGate)
        {
            if (_suspended.Contains(automation.Id))
                return false;

            if (_running.Contains(automation.Id))
            {
                skipped = "it is still running";
            }
            else if (automation.Cooldown is { } cooldown && _lastRun.TryGetValue(automation.Id, out var last) && now - last < cooldown)
            {
                _logger.LogDebug("Automation {Id} is in cooldown", automation.Id);
                return false;
            }
            else
            {
                if (!_recentRuns.TryGetValue(automation.Id, out var runs))
                    _recentRuns[automation.Id] = runs = new Queue<DateTimeOffset>();
                while (runs.Count > 0 && now - runs.Peek() > TimeSpan.FromMinutes(1))
                    runs.Dequeue();
                runs.Enqueue(now);

                if (runs.Count > _options.MaxRunsPerMinute)
                {
                    _suspended.Add(automation.Id);
                    suspendedNow = true;
                }
                else
                {
                    _running.Add(automation.Id);
                    _lastRun[automation.Id] = now;
                    return true;
                }
            }
        }

        if (suspendedNow)
        {
            Log.Error(ActivitySources.Engine,
                $"'{automation.DisplayName}' ran more than {_options.MaxRunsPerMinute} times in a minute and was suspended to prevent a loop. " +
                "Check whether another automation undoes what it does, then resume it or save the configuration again.",
                automation.Id);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (skipped is not null)
        {
            Log.Info(ActivitySources.Automation, $"'{automation.DisplayName}' was triggered ({EventNames.Describe(e)}) but {skipped}.", automation.Id);
        }
        return false;
    }

    private void ReleaseRun(string automationId)
    {
        lock (_runGate)
            _running.Remove(automationId);
    }

    private void StartRun(Automation automation, SystemEvent e, CancellationToken cancellationToken)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                Log.Info(ActivitySources.Automation, $"▶ '{automation.DisplayName}' started: {EventNames.Describe(e)}.", automation.Id);
                await RunActionsAsync(automation, e, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automation {Id} crashed", automation.Id);
                Log.Error(ActivitySources.Automation, $"'{automation.DisplayName}' stopped because of an internal error: {ex.Message}", automation.Id);
            }
            finally
            {
                ReleaseRun(automation.Id);
            }
        }, CancellationToken.None);

        lock (_runGate)
            _inFlight.Add(task);
        task.ContinueWith(t =>
        {
            lock (_runGate)
                _inFlight.Remove(t);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RunActionsAsync(Automation automation, SystemEvent? e, CancellationToken cancellationToken)
    {
        var context = CreateActionContext(e, automation.Id, automation.DisplayName);
        var failures = 0;
        for (var i = 0; i < automation.Actions.Count; i++)
        {
            var action = automation.Actions[i];
            if (await ExecuteActionAsync(action, context, cancellationToken).ConfigureAwait(false))
                continue;
            failures++;
            if (action.GetBoolean(ComponentCatalog.ContinueOnError.Name) != true)
            {
                var remaining = automation.Actions.Count - i - 1;
                Log.Warning(ActivitySources.Automation,
                    $"'{automation.DisplayName}' stopped after a failed action{(remaining > 0 ? $"; {remaining} action(s) skipped" : "")}. Set continue_on_error: true on an action to keep going.",
                    automation.Id);
                return;
            }
        }

        if (failures == 0)
            Log.Success(ActivitySources.Automation, $"✓ '{automation.DisplayName}' finished.", automation.Id);
        else
            Log.Warning(ActivitySources.Automation, $"'{automation.DisplayName}' finished with {failures} failed action(s).", automation.Id);
    }

    private ActionContext CreateActionContext(SystemEvent? e, string? automationId, string? automationName) =>
        new(e, automationId, automationName, Log, DryRun) { CurrentUser = CurrentUser };

    private async Task<(bool Ok, string? Failed)> EvaluateAllAsync(IReadOnlyList<ComponentConfig> conditions, ConditionContext context, CancellationToken cancellationToken)
    {
        for (var i = 0; i < conditions.Count; i++)
        {
            if (!await EvaluateAsync(conditions[i], context, cancellationToken).ConfigureAwait(false))
                return (false, $"condition {i + 1} ({Summarize(conditions[i])})");
        }
        return (true, null);
    }

    private async Task<bool> EvaluateAsync(ComponentConfig condition, ConditionContext context, CancellationToken cancellationToken)
    {
        var effective = Catalog.WithDefaults(ComponentKind.Condition, condition);
        try
        {
            switch (condition.Type)
            {
                case "and":
                    foreach (var nested in effective.GetComponents("conditions"))
                        if (!await EvaluateAsync(nested, context, cancellationToken).ConfigureAwait(false))
                            return false;
                    return true;

                case "or":
                    foreach (var nested in effective.GetComponents("conditions"))
                        if (await EvaluateAsync(nested, context, cancellationToken).ConfigureAwait(false))
                            return true;
                    return false;

                case "not":
                    foreach (var nested in effective.GetComponents("conditions"))
                        if (await EvaluateAsync(nested, context, cancellationToken).ConfigureAwait(false))
                            return false;
                    return true;

                case "user":
                    return UserPattern.MatchesAny(effective.GetStringList("users"), context.Event?.User ?? context.CurrentUser);

                case "time":
                    return TimeCondition.IsMet(effective, _time.GetLocalNow());

                case "app_running":
                    return context.Processes.IsRunning(effective.GetStringList("app")) == (effective.GetBoolean("running") ?? true);

                case "profile_active":
                    return _profiles.IsActive(effective.GetString("profile") ?? "") == (effective.GetBoolean("active") ?? true);
            }

            var handler = _handlers.FindCondition(condition.Type);
            if (handler is null)
            {
                Log.Warning(ActivitySources.Automation, $"Condition '{condition.Type}' is not available here and counts as false.");
                return false;
            }
            return await handler.EvaluateAsync(effective, context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ActivitySources.Automation, $"Condition '{condition.Type}' failed and counts as false: {ex.Message}");
            return false;
        }
    }

    private static string Summarize(ComponentConfig component) => component.ToString();

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>Evaluates the built-in <c>time</c> condition.</summary>
public static class TimeCondition
{
    /// <summary>Whether <paramref name="now"/> is inside the window described by <paramref name="condition"/>.</summary>
    public static bool IsMet(ComponentConfig condition, DateTimeOffset now)
    {
        var weekdays = condition.GetStringList("weekdays");
        if (weekdays.Count > 0)
        {
            var today = BuiltInConditions.Weekdays[((int)now.DayOfWeek + 6) % 7];
            if (!weekdays.Contains(today, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        var time = TimeOnly.FromDateTime(now.DateTime);
        var after = condition.GetTime("after");
        var before = condition.GetTime("before");
        return (after, before) switch
        {
            ({ } a, { } b) when a <= b => time >= a && time < b,
            ({ } a, { } b) => time >= a || time < b,
            ({ } a, null) => time >= a,
            (null, { } b) => time < b,
            _ => true,
        };
    }
}
