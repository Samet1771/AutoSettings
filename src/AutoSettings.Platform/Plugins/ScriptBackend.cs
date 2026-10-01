using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;
using AutoSettings.Core.Plugins;

namespace AutoSettings.Platform.Plugins;

/// <summary>Runs script plugins: every action, condition and poll is one run of a PowerShell file.</summary>
public sealed class ScriptBackend : IPluginBackend
{
    /// <inheritdoc />
    public bool Supports(PluginKind kind) => kind == PluginKind.Script;

    /// <inheritdoc />
    public IActionHandler CreateAction(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context) =>
        new ScriptActionHandler(plugin, component, context);

    /// <inheritdoc />
    public IConditionHandler CreateCondition(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context) =>
        new ScriptConditionHandler(plugin, component, context);

    /// <inheritdoc />
    public IDisposable? StartTriggers(InstalledPlugin plugin, PluginHostContext context, Action<PluginEventReport> raise)
    {
        var pollers = plugin.Manifest!.Components
            .Where(c => c.Kind == Core.Catalog.ComponentKind.Trigger && c.Scripts.Poll is not null)
            .Select(c => new ScriptPoller(plugin, c, context, raise))
            .ToList();
        return pollers.Count == 0 ? null : new Disposables(pollers);
    }

    /// <inheritdoc />
    public void Unload(InstalledPlugin plugin)
    {
    }

    /// <summary>Runs one script of a component and turns problems into exceptions the engine understands.</summary>
    internal static async Task<ScriptResult> RunAsync(
        InstalledPlugin plugin,
        ManifestComponent component,
        string script,
        PluginOperation operation,
        IReadOnlyDictionary<string, object?> parameters,
        SystemEvent? triggerEvent,
        UserInfo? user,
        string? automationName,
        string? snapshot,
        PluginHostContext context,
        CancellationToken cancellationToken,
        bool firstPoll = false)
    {
        var state = context.StateDirectoryFor(plugin.Id);
        var request = PluginRequest.Build(plugin, component.Type, operation, parameters, triggerEvent, user, automationName, snapshot, state, firstPoll);
        var environment = PluginRequest.Environment(plugin, operation, parameters, snapshot, state, firstPoll);
        if (triggerEvent is not null || user is not null)
        {
            // The same placeholder values command.run gets (AUTOSETTINGS_USER, AUTOSETTINGS_EVENT_DATA_DRIVE, ...).
            var placeholderContext = new ActionContext(triggerEvent, null, automationName, context.Log) { CurrentUser = user };
            foreach (var (name, value) in Placeholders.ValuesFor(placeholderContext, DateTimeOffset.Now))
                environment.TryAdd(PluginRequest.VariableName(name), value ?? "");
        }
        try
        {
            return await ScriptRunner.RunAsync(plugin.Directory, script, request, environment, component.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (ScriptException ex)
        {
            throw new ActionFailedException(ex.Message, ex);
        }
    }

    private sealed class Disposables(IEnumerable<IDisposable> items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
                item.Dispose();
        }
    }
}

/// <summary>A script plugin action: runs <c>apply</c>; <c>capture</c> and <c>restore</c> for profiles.</summary>
internal sealed class ScriptActionHandler(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context) : IRevertibleActionHandler
{
    public string Type => component.Type;

    public async Task ExecuteAsync(ComponentConfig action, ActionContext actionContext, CancellationToken cancellationToken)
    {
        var result = await ScriptBackend.RunAsync(plugin, component, component.Scripts.Apply!, PluginOperation.Apply, action.Parameters,
            actionContext.Event, actionContext.User, actionContext.AutomationName, null, context, cancellationToken).ConfigureAwait(false);
        Report(result, actionContext);
    }

    public async Task<string?> CaptureAsync(ComponentConfig action, ActionContext actionContext, CancellationToken cancellationToken)
    {
        if (component.Scripts.Capture is not { } script)
            return null;
        var result = await ScriptBackend.RunAsync(plugin, component, script, PluginOperation.Capture, action.Parameters,
            actionContext.Event, actionContext.User, actionContext.AutomationName, null, context, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new ActionFailedException($"{component.Type} could not read the current value: {result.FailureMessage}");
        return PluginOutput.ParseSnapshot(result.Output);
    }

    public async Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext actionContext, CancellationToken cancellationToken)
    {
        if (component.Scripts.Restore is not { } script)
            return;
        var result = await ScriptBackend.RunAsync(plugin, component, script, PluginOperation.Restore, action.Parameters,
            actionContext.Event, actionContext.User, actionContext.AutomationName, snapshot, context, cancellationToken).ConfigureAwait(false);
        Report(result, actionContext);
    }

    private void Report(ScriptResult result, ActionContext actionContext)
    {
        if (result.ExitCode != 0)
            throw new ActionFailedException(result.FailureMessage);
        var output = result.Output.Trim();
        if (output.Length > 0)
            actionContext.Log.Info(ActivitySources.Plugin, $"{component.Type}: {(output.Length <= 400 ? output : "…" + output[^400..])}", actionContext.AutomationId);
    }
}

/// <summary>A script plugin condition: runs <c>evaluate</c>, which prints <c>true</c> or <c>false</c>.</summary>
internal sealed class ScriptConditionHandler(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context) : IConditionHandler
{
    public string Type => component.Type;

    public async ValueTask<bool> EvaluateAsync(ComponentConfig condition, ConditionContext conditionContext, CancellationToken cancellationToken)
    {
        var result = await ScriptBackend.RunAsync(plugin, component, component.Scripts.Evaluate!, PluginOperation.Evaluate, condition.Parameters,
            conditionContext.Event, conditionContext.Event?.User ?? conditionContext.CurrentUser, null, null, context, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new ActionFailedException(result.FailureMessage);
        return PluginOutput.ParseCondition(result.Output)
            ?? throw new ActionFailedException($"{component.Type} must print true or false as its last line");
    }
}

/// <summary>Runs a trigger's <c>poll</c> script every interval and raises the events it prints.</summary>
internal sealed class ScriptPoller : IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _stop = new();

    public ScriptPoller(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context, Action<PluginEventReport> raise)
    {
        var token = _stop.Token;
        _ = Task.Run(() => RunAsync(plugin, component, context, raise, token));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private static async Task RunAsync(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context, Action<PluginEventReport> raise, CancellationToken stop)
    {
        var interval = component.Interval ?? DefaultInterval;
        var first = true;
        var failing = false;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var result = await ScriptBackend.RunAsync(plugin, component, component.Scripts.Poll!, PluginOperation.Poll,
                    new Dictionary<string, object?>(), null, context.User, null, null, context, stop, first).ConfigureAwait(false);
                if (result.ExitCode != 0)
                    throw new ActionFailedException(result.FailureMessage);
                var reports = PluginOutput.ParseEvents(plugin.Id, result.Output, out var ignored);
                foreach (var report in reports)
                    raise(report);
                foreach (var reason in ignored)
                    context.Log.Warning(ActivitySources.Plugin, $"{component.Type}: ignored output {reason}");
                if (failing)
                    context.Log.Info(ActivitySources.Plugin, $"{component.Type} works again.");
                failing = false;
                first = false;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Report once, not every few seconds.
                if (!failing)
                    context.Log.Error(ActivitySources.Plugin, $"{component.Type} could not check for events: {ex.Message}. It keeps trying every {ValueConverter.FormatDuration(interval)}.");
                failing = true;
            }

            try
            {
                await Task.Delay(interval, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
