using AutoSettings.Core.Catalog;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Ipc;
using AutoSettings.Core.Model;

namespace AutoSettings.Service;

/// <summary>
/// Runs an action for a machine automation: actions that need SYSTEM rights run in the service;
/// actions that change a user's own settings are sent to the agent of the user the event belongs to.
/// </summary>
internal sealed class RoutingActionHandler : IRevertibleActionHandler
{
    private readonly ComponentDescriptor _descriptor;
    private readonly IActionHandler? _local;
    private readonly AgentHub _hub;

    public RoutingActionHandler(ComponentDescriptor descriptor, IActionHandler? local, AgentHub hub)
    {
        _descriptor = descriptor;
        _local = local;
        _hub = hub;
    }

    public string Type => _descriptor.Type;

    public async Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        if (RunsInService(action))
        {
            await Local().ExecuteAsync(action, context, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!await Agent(context).ExecuteActionAsync(ToRemote(action, context), cancellationToken).ConfigureAwait(false))
            throw new ActionFailedException("it failed in the user's session (see the user's activity log for details)");
    }

    public async Task<string?> CaptureAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        if (RunsInService(action))
            return Local() is IRevertibleActionHandler revertible ? await revertible.CaptureAsync(action, context, cancellationToken).ConfigureAwait(false) : null;
        return await Agent(context).CaptureActionAsync(ToRemote(action, context), cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext context, CancellationToken cancellationToken)
    {
        if (RunsInService(action))
        {
            if (Local() is IRevertibleActionHandler revertible)
                await revertible.RestoreAsync(action, snapshot, context, cancellationToken).ConfigureAwait(false);
            return;
        }
        await Agent(context).RestoreActionAsync(ToRemote(action, context), snapshot, cancellationToken).ConfigureAwait(false);
    }

    private bool RunsInService(ComponentConfig action) => _descriptor.ResolveRunsAs(action) == ExecutionScope.Machine;

    private IActionHandler Local() =>
        _local ?? throw new ActionFailedException($"'{Type}' cannot run in the service");

    private IAgentApi Agent(ActionContext context)
    {
        var sessionId = context.Event?.SessionId;
        if (sessionId is null)
        {
            var what = context.Event is { } e ? $"the '{EventNames.TriggerType(e.Kind)}' event" : "a manual run";
            throw new ActionFailedException(
                $"'{Type}' changes a user's own settings, but {what} does not belong to a signed-in user. Use a logon or app trigger, or move the automation to your personal automations.");
        }
        return _hub.For(sessionId)
            ?? throw new ActionFailedException($"the {Core.Product.Name} agent is not running in session {sessionId}, so '{Type}' cannot change that user's settings");
    }

    private static RemoteAction ToRemote(ComponentConfig action, ActionContext context) =>
        new(action.Type, PlainJson.Serialize(action.Parameters), context.Event, context.AutomationId, context.AutomationName);
}
