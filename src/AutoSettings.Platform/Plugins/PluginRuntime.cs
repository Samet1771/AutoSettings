using AutoSettings.Core;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;
using AutoSettings.Core.Plugins;
using AutoSettings.Core.Updates;
using AutoSettings.Platform.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoSettings.Platform.Plugins;

/// <summary>Where plugins run: the service (as SYSTEM) or a user's app.</summary>
/// <param name="Scope">The service (<see cref="ExecutionScope.Machine"/>) or an agent (<see cref="ExecutionScope.User"/>).</param>
/// <param name="Log">The activity log.</param>
/// <param name="User">For agents: the signed-in user.</param>
/// <param name="SessionId">For agents: the Windows session.</param>
/// <param name="StateRoot">Where plugins keep data; defaults to the plugin-data folder of the scope (tests use a temporary folder).</param>
public sealed record PluginHostContext(ExecutionScope Scope, ActivityLog Log, UserInfo? User = null, int? SessionId = null, string? StateRoot = null)
{
    /// <summary>A folder where a plugin may keep data between runs (created on demand).</summary>
    public string StateDirectoryFor(string pluginId)
    {
        var root = StateRoot ?? (Scope == ExecutionScope.Machine ? Product.MachinePluginDataDirectory : Product.UserPluginDataDirectory);
        var path = Path.Combine(root, pluginId);
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The plugin will report it if it needs the folder.
        }
        return path;
    }
}

/// <summary>Runs one kind of plugin (script or .NET).</summary>
public interface IPluginBackend
{
    /// <summary>Whether this backend runs plugins of <paramref name="kind"/>.</summary>
    bool Supports(PluginKind kind);

    /// <summary>The handler of an action component.</summary>
    IActionHandler CreateAction(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context);

    /// <summary>The handler of a condition component.</summary>
    IConditionHandler CreateCondition(InstalledPlugin plugin, ManifestComponent component, PluginHostContext context);

    /// <summary>Starts watching for the plugin's events; dispose the result to stop. <c>null</c> when it has nothing to watch.</summary>
    IDisposable? StartTriggers(InstalledPlugin plugin, PluginHostContext context, Action<PluginEventReport> raise);

    /// <summary>Releases what the plugin uses (called before it is removed, turned off or updated).</summary>
    void Unload(InstalledPlugin plugin);
}

/// <summary>A folder of plugins and whether its plugins are for the machine or for the user.</summary>
/// <param name="Path">The folder.</param>
/// <param name="Scope">Machine or user plugins.</param>
public sealed record PluginRoot(string Path, ExecutionScope Scope);

/// <summary>
/// Loads the plugins of the service or of an agent, registers their handlers, builds the catalog and runs their triggers.
/// <list type="bullet">
/// <item>The service loads machine plugins. It runs their <c>runs_as: machine</c> actions (see <see cref="MachineActions"/>)
/// and their conditions as SYSTEM, and only from folders that only administrators can change.</item>
/// <item>An agent loads machine and user plugins and runs their user actions and conditions as the user.</item>
/// </list>
/// Triggers run where the automations that use them run, and only while an automation uses one of the plugin's triggers.
/// </summary>
public sealed class PluginRuntime : IDisposable
{
    private readonly PluginHostContext _context;
    private readonly IReadOnlyList<PluginRoot> _roots;
    private readonly HandlerRegistry _handlers;
    private readonly IReadOnlyList<IPluginBackend> _backends;
    private readonly Action<SystemEvent> _post;
    private readonly SemVersion? _appVersion;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly List<string> _registeredActions = [];
    private readonly List<string> _registeredConditions = [];
    private readonly Dictionary<string, IDisposable> _triggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _debounce;
    private AutomationConfig _config = new();
    private Dictionary<string, IActionHandler> _machineActions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the runtime. Call <see cref="Load"/> to load the plugins.</summary>
    public PluginRuntime(
        PluginHostContext context,
        IReadOnlyList<PluginRoot> roots,
        HandlerRegistry handlers,
        IReadOnlyList<IPluginBackend> backends,
        Action<SystemEvent> post,
        SemVersion? appVersion,
        ILogger? logger = null)
    {
        _context = context;
        _roots = roots;
        _handlers = handlers;
        _backends = backends;
        _post = post;
        _appVersion = appVersion;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The plugins found, valid or not, turned on or not.</summary>
    public IReadOnlyList<InstalledPlugin> Plugins { get; private set; } = [];

    /// <summary>The catalog with the built-in components and the components of the loaded plugins.</summary>
    public ComponentCatalog Catalog { get; private set; } = ComponentCatalog.BuiltIn;

    /// <summary>For the service: handlers of plugin actions that run as SYSTEM, by type.</summary>
    public IReadOnlyDictionary<string, IActionHandler> MachineActions => _machineActions;

    /// <summary>Raised after plugins were loaded again because the plugin folders changed. May be raised on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Finds the plugins, registers their handlers and builds <see cref="Catalog"/>. Problems are written to the activity
    /// log; a broken plugin never stops the others.
    /// </summary>
    public void Load()
    {
        lock (_gate)
        {
            foreach (var plugin in Plugins.Where(p => p.IsActive))
                BackendFor(plugin)?.Unload(plugin);

            var found = new List<InstalledPlugin>();
            foreach (var root in _roots)
            {
                // Plugins uninstalled while their host was running can be deleted now that it stopped.
                try
                {
                    PluginInstaller.FinishRemovals(root.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Cannot finish removals in {Path}", root.Path);
                }

                foreach (var plugin in PluginStore.Discover(root.Path, root.Scope, _appVersion))
                    found.Add(CheckFolder(plugin, root));
            }

            var composition = PluginStore.Compose(found.Where(p => BackendFor(p) is not null));
            var rejected = composition.Rejected.Select(r => r.PluginId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var loaded = found.Where(p => p.IsActive && BackendFor(p) is not null && !rejected.Contains(p.Id)).ToList();

            foreach (var type in _registeredActions)
                _handlers.RemoveAction(type);
            foreach (var type in _registeredConditions)
                _handlers.RemoveCondition(type);
            _registeredActions.Clear();
            _registeredConditions.Clear();
            var machineActions = new Dictionary<string, IActionHandler>(StringComparer.OrdinalIgnoreCase);

            foreach (var plugin in loaded)
            {
                var backend = BackendFor(plugin)!;
                foreach (var component in plugin.Manifest!.Components)
                {
                    try
                    {
                        Register(plugin, component, backend, machineActions);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Could not load {Type}", component.Type);
                        _context.Log.Error(ActivitySources.Plugin, $"Could not load {component.Type} from {plugin.DisplayName}: {ex.Message}");
                    }
                }
            }
            _machineActions = machineActions;

            Report(found, composition.Rejected, loaded);
            Plugins = found;
            Catalog = composition.Catalog;
            RestartTriggers();
        }
    }

    /// <summary>Loads the plugins again when the plugin folders change (debounced).</summary>
    public void StartWatching()
    {
        foreach (var root in _roots)
        {
            try
            {
                Directory.CreateDirectory(root.Path);
                var watcher = new FileSystemWatcher(root.Path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                };
                watcher.Changed += (_, _) => Schedule();
                watcher.Created += (_, _) => Schedule();
                watcher.Deleted += (_, _) => Schedule();
                watcher.Renamed += (_, _) => Schedule();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogWarning(ex, "Cannot watch {Path}", root.Path);
            }
        }
    }

    /// <summary>
    /// Tells the runtime which automations run now, so it runs the triggers of exactly the plugins they use.
    /// Call it after every configuration load.
    /// </summary>
    public void UseConfig(AutomationConfig config)
    {
        lock (_gate)
        {
            _config = config;
            RestartTriggers();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var watcher in _watchers)
                watcher.Dispose();
            _watchers.Clear();
            _debounce?.Dispose();
            StopTriggers();
            foreach (var plugin in Plugins.Where(p => p.IsActive))
                BackendFor(plugin)?.Unload(plugin);
        }
    }

    private void Schedule()
    {
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                try
                {
                    Load();
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reloading plugins failed");
                }
            }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }
    }

    private InstalledPlugin CheckFolder(InstalledPlugin plugin, PluginRoot root)
    {
        // Machine plugins run as SYSTEM in the service: refuse folders that someone other than an administrator can change.
        if (_context.Scope != ExecutionScope.Machine || root.Scope != ExecutionScope.Machine || !plugin.IsValid)
            return plugin;
        if (SecureDirectory.IsAdminOnly(plugin.Directory, out var problem))
            return plugin;
        plugin.Issues.Add(new ConfigIssue(IssueSeverity.Error, $"Not loaded for safety: {problem}"));
        return plugin;
    }

    private void Register(InstalledPlugin plugin, ManifestComponent component, IPluginBackend backend, Dictionary<string, IActionHandler> machineActions)
    {
        switch (component.Kind)
        {
            case ComponentKind.Action when component.RunsAs == ExecutionScope.Machine:
                // Only the service runs these, and only for machine plugins (the validator enforces both).
                if (_context.Scope == ExecutionScope.Machine && plugin.Scope == ExecutionScope.Machine)
                    machineActions[component.Type] = backend.CreateAction(plugin, component, _context);
                break;
            case ComponentKind.Action:
                // The service sends user actions to the user's agent, which has its own handler.
                if (_context.Scope == ExecutionScope.User)
                {
                    _handlers.Add(backend.CreateAction(plugin, component, _context));
                    _registeredActions.Add(component.Type);
                }
                break;
            case ComponentKind.Condition:
                _handlers.Add(backend.CreateCondition(plugin, component, _context));
                _registeredConditions.Add(component.Type);
                break;
        }
    }

    private void Report(List<InstalledPlugin> found, IReadOnlyList<PluginConflict> rejected, List<InstalledPlugin> loaded)
    {
        var where = _context.Scope == ExecutionScope.Machine ? "the service" : "this app";
        foreach (var plugin in found)
        {
            foreach (var issue in plugin.Issues.Where(i => i.Severity == IssueSeverity.Error))
                _context.Log.Error(ActivitySources.Plugin, $"Plugin {plugin.DisplayName}: {issue.Message}");
            if (plugin.IsActive && BackendFor(plugin) is null)
                _context.Log.Warning(ActivitySources.Plugin, $"Plugin {plugin.DisplayName}: {plugin.Manifest!.Kind} plugins are not supported by this version.");
        }
        foreach (var conflict in rejected)
            _context.Log.Error(ActivitySources.Plugin, $"Plugin {conflict.PluginId} was not loaded: {conflict.Reason}");
        var previous = Plugins.Where(p => p.IsActive).Select(p => $"{p.Id} {p.Manifest!.Version}").ToHashSet();
        var current = loaded.Select(p => $"{p.Id} {p.Manifest!.Version}").ToHashSet();
        if (!previous.SetEquals(current) && current.Count > 0)
            _context.Log.Info(ActivitySources.Plugin, $"Plugins loaded in {where}: {string.Join(", ", current.Order())}.");
        else if (!previous.SetEquals(current))
            _context.Log.Info(ActivitySources.Plugin, $"No plugins are loaded in {where}.");
    }

    private void RestartTriggers()
    {
        StopTriggers();
        var used = _config.Automations
            .Where(a => a.Enabled && !a.IsBlocked)
            .SelectMany(a => a.Triggers)
            .Select(t => PluginIds.PluginOf(t.Type))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in Plugins.Where(p => p.IsActive && used.Contains(p.Id)))
        {
            if (BackendFor(plugin) is not { } backend || Catalog.All.All(d => d.Source.PluginId != plugin.Id))
                continue;
            try
            {
                if (backend.StartTriggers(plugin, _context, report => Raise(plugin, report)) is { } running)
                    _triggers[plugin.Id] = running;
            }
            catch (Exception ex)
            {
                _context.Log.Error(ActivitySources.Plugin, $"Could not start the triggers of {plugin.DisplayName}: {ex.Message}");
            }
        }
    }

    private void StopTriggers()
    {
        foreach (var running in _triggers.Values)
        {
            try
            {
                running.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stopping plugin triggers failed");
            }
        }
        _triggers.Clear();
    }

    private void Raise(InstalledPlugin plugin, PluginEventReport report)
    {
        var user = report.User is { Length: > 0 } name
            ? (_context.User is { } me && string.Equals(me.Name, name, StringComparison.OrdinalIgnoreCase) ? me : new UserInfo(name))
            : _context.User;
        _post(new SystemEvent
        {
            Kind = SystemEventKind.Plugin,
            PluginEvent = report.Name,
            Data = report.Data,
            User = user,
            SessionId = _context.SessionId,
        });
    }

    private IPluginBackend? BackendFor(InstalledPlugin plugin) =>
        plugin.Manifest is { } manifest ? _backends.FirstOrDefault(b => b.Supports(manifest.Kind)) : null;
}
