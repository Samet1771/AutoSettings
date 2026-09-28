using AutoSettings.Core.Events;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Engine;

/// <summary>Information available to an action while it runs.</summary>
public sealed class ActionContext
{
    /// <summary>Creates a context.</summary>
    public ActionContext(SystemEvent? triggerEvent, string? automationId, string? automationName, ActivityLog log, bool dryRun = false)
    {
        Event = triggerEvent;
        AutomationId = automationId;
        AutomationName = automationName;
        Log = log;
        DryRun = dryRun;
    }

    /// <summary>The event that started the automation (null for manual runs).</summary>
    public SystemEvent? Event { get; }

    /// <summary>Id of the automation running the action, if any.</summary>
    public string? AutomationId { get; }

    /// <summary>Display name of the automation (or profile) running the action.</summary>
    public string? AutomationName { get; }

    /// <summary>The activity log.</summary>
    public ActivityLog Log { get; }

    /// <summary>When true, actions are logged instead of executed.</summary>
    public bool DryRun { get; }

    /// <summary>The signed-in user the engine runs for (agents only).</summary>
    public UserInfo? CurrentUser { get; init; }

    /// <summary>The user the action concerns: the event's user, otherwise the current user.</summary>
    public UserInfo? User => Event?.User ?? CurrentUser;
}

/// <summary>Information available while evaluating a condition.</summary>
public sealed class ConditionContext
{
    /// <summary>Creates a context.</summary>
    public ConditionContext(SystemEvent? triggerEvent, UserInfo? currentUser, ProcessTracker processes, TimeProvider time)
    {
        Event = triggerEvent;
        CurrentUser = currentUser;
        Processes = processes;
        Time = time;
    }

    /// <summary>The event being evaluated.</summary>
    public SystemEvent? Event { get; }

    /// <summary>The signed-in user the engine runs for (agents only).</summary>
    public UserInfo? CurrentUser { get; }

    /// <summary>Known running processes.</summary>
    public ProcessTracker Processes { get; }

    /// <summary>Clock.</summary>
    public TimeProvider Time { get; }
}

/// <summary>Executes one action type, e.g. <c>audio.volume</c>.</summary>
public interface IActionHandler
{
    /// <summary>The action type this handler executes.</summary>
    string Type { get; }

    /// <summary>
    /// Runs the action. Parameters are typed, defaults are filled in and placeholders are replaced.
    /// Throw (for example <see cref="ActionFailedException"/>) to report a failure.
    /// </summary>
    Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken);
}

/// <summary>An action whose effect can be undone. Used by profiles.</summary>
public interface IRevertibleActionHandler : IActionHandler
{
    /// <summary>
    /// Reads the current value of the setting that <paramref name="action"/> would change and returns it
    /// in any text form the handler understands (it must survive being sent over IPC).
    /// </summary>
    Task<string?> CaptureAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken);

    /// <summary>Restores a value previously returned by <see cref="CaptureAsync"/>.</summary>
    Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext context, CancellationToken cancellationToken);
}

/// <summary>Evaluates one condition type, e.g. <c>power_source</c>.</summary>
public interface IConditionHandler
{
    /// <summary>The condition type this handler evaluates.</summary>
    string Type { get; }

    /// <summary>Returns whether the condition is currently true. Parameters are typed and defaults are filled in.</summary>
    ValueTask<bool> EvaluateAsync(ComponentConfig condition, ConditionContext context, CancellationToken cancellationToken);
}

/// <summary>An action failed for a reason that can be explained to the user.</summary>
public sealed class ActionFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ActionFailedException(string message) : base(message) { }

    /// <summary>Creates the exception with an inner exception.</summary>
    public ActionFailedException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Maps component types to their handlers.</summary>
public sealed class HandlerRegistry
{
    private readonly Dictionary<string, IActionHandler> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IConditionHandler> _conditions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers (or replaces) an action handler.</summary>
    public HandlerRegistry Add(IActionHandler handler)
    {
        _actions[handler.Type] = handler;
        return this;
    }

    /// <summary>Registers (or replaces) a condition handler.</summary>
    public HandlerRegistry Add(IConditionHandler handler)
    {
        _conditions[handler.Type] = handler;
        return this;
    }

    /// <summary>Finds an action handler.</summary>
    public IActionHandler? FindAction(string type) => _actions.GetValueOrDefault(type);

    /// <summary>Finds a condition handler.</summary>
    public IConditionHandler? FindCondition(string type) => _conditions.GetValueOrDefault(type);

    /// <summary>Registered action types.</summary>
    public IEnumerable<string> ActionTypes => _actions.Keys;

    /// <summary>Registered condition types.</summary>
    public IEnumerable<string> ConditionTypes => _conditions.Keys;
}
