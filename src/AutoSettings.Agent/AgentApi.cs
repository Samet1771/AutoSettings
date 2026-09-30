using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Ipc;
using AutoSettings.Core.Model;
using AutoSettings.Core.Updates;
using AutoSettings.Platform.Monitoring;

namespace AutoSettings.Agent;

/// <summary>What the service can ask the agent to do.</summary>
internal sealed class AgentApi : IAgentApi
{
    private readonly AgentHost _host;
    
    public AgentApi(AgentHost host) => _host = host;

    public Task OnSystemEventAsync(SystemEvent systemEvent, CancellationToken cancellationToken)
    {
        _host.Engine.Post(systemEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExecuteActionAsync(RemoteAction action, CancellationToken cancellationToken)
    {
        if (ToComponent(action) is not { } component)
            return Task.FromResult(false);
        var context = new ActionContext(action.Event, action.AutomationId, $"machine automation '{action.AutomationName}'", _host.Activity)
        {
            CurrentUser = _host.User,
        };
        return _host.Engine.ExecuteActionAsync(component, context, cancellationToken);
    }

    public async Task<string?> CaptureActionAsync(RemoteAction action, CancellationToken cancellationToken)
    {
        if (ToComponent(action) is not { } component || _host.Handlers.FindAction(action.Type) is not IRevertibleActionHandler handler)
            return null;
        return await handler.CaptureAsync(WithDefaults(component), Context(action), cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreActionAsync(RemoteAction action, string? snapshot, CancellationToken cancellationToken)
    {
        if (ToComponent(action) is not { } component || _host.Handlers.FindAction(action.Type) is not IRevertibleActionHandler handler)
            return;
        await handler.RestoreAsync(WithDefaults(component), snapshot, Context(action), cancellationToken).ConfigureAwait(false);
    }

    public Task OnUpdateStatusChangedAsync(UpdateStatus status, CancellationToken cancellationToken)
    {
        _host.SetUpdateStatus(status);
        return Task.CompletedTask;
    }

    public Task<bool> IsBusyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ForegroundMonitor.IsFullScreenAppActive());

    private ComponentConfig? ToComponent(RemoteAction action)
    {
        var component = new ComponentConfig(action.Type, PlainJson.Deserialize(action.ParametersJson));
        // Validate as a personal action: the service can never make the agent do what the user could not.
        var errors = new ConfigValidator(AgentCatalog.Current).NormalizeComponent(ComponentKind.Action, component, ExecutionScope.User)
            .Where(i => i.Severity == IssueSeverity.Error)
            .ToList();
        if (errors.Count == 0)
            return component;
        _host.Activity.Error(ActivitySources.Action, $"Rejected {action.Type} from machine automation '{action.AutomationName}': {errors[0].Message}");
        return null;
    }

    private static ComponentConfig WithDefaults(ComponentConfig component) =>
        AgentCatalog.Current.WithDefaults(ComponentKind.Action, component);

    private ActionContext Context(RemoteAction action) =>
        new(action.Event, action.AutomationId, $"machine automation '{action.AutomationName}'", _host.Activity) { CurrentUser = _host.User };
}
