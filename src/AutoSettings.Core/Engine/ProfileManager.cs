using AutoSettings.Core.Catalog;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Engine;

/// <summary>An applied profile, as shown in the UI.</summary>
/// <param name="Id">Profile id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Priority">Profile priority.</param>
/// <param name="AppliedAt">When it was applied.</param>
/// <param name="RevertsWhen">Plain-language automatic revert rule, or null when it stays until reverted manually.</param>
public sealed record ActiveProfileInfo(string Id, string Name, int Priority, DateTimeOffset AppliedAt, string? RevertsWhen);

/// <summary>
/// Applies and reverts profiles. Keeps a stack of active profiles and the original ("baseline")
/// value of every setting they changed, so that overlapping profiles resolve predictably:
/// <list type="bullet">
/// <item>The active profile with the highest priority (then the most recently applied) owns a setting.</item>
/// <item>Reverting the owner applies the next owner's value, or the baseline when no profile changes the setting any more.</item>
/// <item>Reverting a profile that does not own a setting changes nothing for that setting.</item>
/// </list>
/// </summary>
internal sealed class ProfileManager
{
    private sealed class Entry
    {
        public required Profile Profile { get; init; }
        public required long Sequence { get; init; }
        public required DateTimeOffset AppliedAt { get; init; }
        public RevertRule? Revert { get; set; }
        public List<(string Key, ComponentConfig Action, IRevertibleActionHandler Handler)> Settings { get; } = [];
    }

    private sealed record Baseline(ComponentConfig Action, IRevertibleActionHandler Handler, string? Snapshot, bool Captured);

    private readonly Func<ComponentCatalog> _catalog;
    private readonly HandlerRegistry _handlers;
    private readonly ActivityLog _log;
    private readonly TimeProvider _time;
    private readonly Func<ComponentConfig, ActionContext, CancellationToken, Task<bool>> _runAction;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _listGate = new();
    private readonly List<Entry> _active = [];
    private readonly Dictionary<string, Baseline> _baselines = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;

    public ProfileManager(
        Func<ComponentCatalog> catalog,
        HandlerRegistry handlers,
        ActivityLog log,
        TimeProvider time,
        Func<ComponentConfig, ActionContext, CancellationToken, Task<bool>> runAction)
    {
        _catalog = catalog;
        _handlers = handlers;
        _log = log;
        _time = time;
        _runAction = runAction;
    }

    /// <summary>Raised after a profile is applied or reverted.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ActiveProfileInfo> Active
    {
        get
        {
            lock (_listGate)
            {
                return _active
                    .OrderByDescending(e => e.Profile.Priority).ThenByDescending(e => e.Sequence)
                    .Select(e => new ActiveProfileInfo(e.Profile.Id, e.Profile.DisplayName, e.Profile.Priority, e.AppliedAt, e.Revert?.Describe()))
                    .ToList();
            }
        }
    }

    public bool IsActive(string profileId)
    {
        lock (_listGate)
            return _active.Any(e => string.Equals(e.Profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ApplyAsync(Profile profile, ActionContext context, RevertRule? revert, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = Find(profile.Id);
            if (existing is not null)
            {
                existing.Revert = revert;
                _log.Info(ActivitySources.Profile,
                    $"Profile '{profile.DisplayName}' is already active{(revert is null ? "" : $"; it will revert {revert.Describe()}")}.",
                    context.AutomationId);
                return;
            }

            var entry = new Entry
            {
                Profile = profile,
                Sequence = ++_sequence,
                AppliedAt = _time.GetLocalNow(),
                Revert = revert,
            };
            var profileContext = new ActionContext(context.Event, context.AutomationId, $"profile '{profile.DisplayName}'", context.Log, context.DryRun)
            {
                CurrentUser = context.CurrentUser,
            };

            foreach (var action in profile.Actions)
            {
                if (action.Type is BuiltInActions.ProfileApply or BuiltInActions.ProfileRevert)
                {
                    _log.Warning(ActivitySources.Profile, $"Skipped {action.Type} in '{profile.DisplayName}': profiles cannot apply or revert other profiles.", context.AutomationId);
                    continue;
                }

                var catalog = _catalog();
                var descriptor = catalog.Find(ComponentKind.Action, action.Type);
                var effective = catalog.WithDefaults(ComponentKind.Action, action);

                if (descriptor is { Revertible: true } && _handlers.FindAction(action.Type) is IRevertibleActionHandler handler)
                {
                    var key = descriptor.SettingKey(effective);
                    var owner = Owner(key);
                    if (!_baselines.ContainsKey(key))
                        _baselines[key] = await CaptureAsync(effective, handler, profileContext, cancellationToken).ConfigureAwait(false);
                    entry.Settings.Add((key, effective, handler));

                    if (owner is null || Compare(entry, owner) > 0)
                        await _runAction(action, profileContext, cancellationToken).ConfigureAwait(false);
                    else
                        _log.Info(ActivitySources.Profile,
                            $"Skipped {action.Type} in '{profile.DisplayName}': active profile '{owner.Profile.DisplayName}' has a higher priority.",
                            context.AutomationId);
                }
                else
                {
                    await _runAction(action, profileContext, cancellationToken).ConfigureAwait(false);
                }
            }

            lock (_listGate)
                _active.Add(entry);

            _log.Success(ActivitySources.Profile,
                $"Profile '{profile.DisplayName}' applied{(revert is null ? "" : $"; it will revert {revert.Describe()}")}.",
                context.AutomationId);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> RevertAsync(string profileId, ActionContext context, string reason, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = Find(profileId);
            if (entry is null)
                return false;

            lock (_listGate)
                _active.Remove(entry);

            var revertContext = new ActionContext(context.Event, context.AutomationId, $"revert of '{entry.Profile.DisplayName}'", context.Log, context.DryRun)
            {
                CurrentUser = context.CurrentUser,
            };

            for (var i = entry.Settings.Count - 1; i >= 0; i--)
            {
                var key = entry.Settings[i].Key;
                var stillOwnedAbove = ActiveSnapshot().Any(other => other.Settings.Any(s => s.Key == key) && Compare(other, entry) > 0);
                if (stillOwnedAbove)
                    continue;

                var next = Owner(key);
                if (next is not null)
                {
                    var nextAction = next.Settings.First(s => s.Key == key).Action;
                    await _runAction(nextAction, revertContext, cancellationToken).ConfigureAwait(false);
                }
                else if (_baselines.Remove(key, out var baseline))
                {
                    await RestoreAsync(baseline, revertContext, cancellationToken).ConfigureAwait(false);
                }
            }

            _log.Success(ActivitySources.Profile, $"Profile '{entry.Profile.DisplayName}' reverted ({reason}).", context.AutomationId);
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Reverts every active profile whose revert rule matches <paramref name="e"/>.</summary>
    public async Task ProcessEventAsync(SystemEvent e, EventFacts facts, ActionContext context, CancellationToken cancellationToken)
    {
        var matching = ActiveSnapshot()
            .Where(entry => entry.Revert?.Matches(e, facts) == true)
            .OrderByDescending(entry => entry.Sequence)
            .ToList();
        foreach (var entry in matching)
            await RevertAsync(entry.Profile.Id, context, entry.Revert!.Describe().Replace("when ", ""), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reverts all active profiles, most recent first.</summary>
    public async Task RevertAllAsync(ActionContext context, string reason, CancellationToken cancellationToken)
    {
        foreach (var entry in ActiveSnapshot().OrderByDescending(entry => entry.Sequence))
            await RevertAsync(entry.Profile.Id, context, reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Baseline> CaptureAsync(ComponentConfig action, IRevertibleActionHandler handler, ActionContext context, CancellationToken cancellationToken)
    {
        if (context.DryRun)
            return new Baseline(action, handler, null, Captured: false);
        try
        {
            var snapshot = await handler.CaptureAsync(action, context, cancellationToken).ConfigureAwait(false);
            return new Baseline(action, handler, snapshot, Captured: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning(ActivitySources.Profile,
                $"Could not read the current value for {action.Type}; it will not be restored when the profile is reverted. {ex.Message}",
                context.AutomationId);
            return new Baseline(action, handler, null, Captured: false);
        }
    }

    private async Task RestoreAsync(Baseline baseline, ActionContext context, CancellationToken cancellationToken)
    {
        if (!baseline.Captured)
            return;
        if (context.DryRun)
        {
            _log.Info(ActivitySources.Action, $"[dry run] Would restore {baseline.Action.Type}.", context.AutomationId);
            return;
        }
        try
        {
            await baseline.Handler.RestoreAsync(baseline.Action, baseline.Snapshot, context, cancellationToken).ConfigureAwait(false);
            _log.Success(ActivitySources.Action, $"Restored {baseline.Action.Type} to its previous value.", context.AutomationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ActivitySources.Action, $"Could not restore {baseline.Action.Type}: {ex.Message}", context.AutomationId);
        }
    }

    private Entry? Find(string profileId)
    {
        lock (_listGate)
            return _active.FirstOrDefault(e => string.Equals(e.Profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
    }

    private List<Entry> ActiveSnapshot()
    {
        lock (_listGate)
            return _active.ToList();
    }

    private Entry? Owner(string key) =>
        ActiveSnapshot()
            .Where(e => e.Settings.Any(s => s.Key == key))
            .OrderByDescending(e => e.Profile.Priority)
            .ThenByDescending(e => e.Sequence)
            .FirstOrDefault();

    private static int Compare(Entry a, Entry b) =>
        a.Profile.Priority != b.Profile.Priority
            ? a.Profile.Priority.CompareTo(b.Profile.Priority)
            : a.Sequence.CompareTo(b.Sequence);
}
