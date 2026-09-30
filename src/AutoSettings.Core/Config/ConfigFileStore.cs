using AutoSettings.Core.Catalog;
using AutoSettings.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoSettings.Core.Config;

/// <summary>
/// Owns one <c>automations.yaml</c> file: loads it, reloads it when it changes on disk, and keeps
/// the last valid configuration running while the file has errors.
/// </summary>
public sealed class ConfigFileStore : IDisposable
{
    private readonly ExecutionScope _scope;
    private volatile ComponentCatalog _catalog;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>Creates a store for <paramref name="path"/>.</summary>
    public ConfigFileStore(string path, ExecutionScope scope, ComponentCatalog? catalog = null, ILogger? logger = null)
    {
        FilePath = path;
        _scope = scope;
        _catalog = catalog ?? ComponentCatalog.BuiltIn;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Full path of the file.</summary>
    public string FilePath { get; }

    /// <summary>The last configuration that loaded without errors.</summary>
    public AutomationConfig Current { get; private set; } = new();

    /// <summary>The result of the most recent load attempt (may contain errors).</summary>
    public ConfigLoadResult? LastResult { get; private set; }

    /// <summary>Raised after every load attempt, valid or not. May be raised on a background thread.</summary>
    public event EventHandler<ConfigLoadResult>? Loaded;

    /// <summary>The catalog the file is validated against.</summary>
    public ComponentCatalog Catalog => _catalog;

    /// <summary>
    /// Switches to another catalog, for example after a plugin was installed or removed, and loads the file
    /// again so that automations using the plugin's components start or stop working.
    /// </summary>
    public ConfigLoadResult UseCatalog(ComponentCatalog catalog)
    {
        _catalog = catalog;
        return Load();
    }

    /// <summary>Creates the folder and a starter file when the file does not exist yet.</summary>
    public void EnsureExists(string starterContent)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        if (!File.Exists(FilePath))
        {
            File.WriteAllText(FilePath, starterContent);
            _logger.LogInformation("Created starter configuration {Path}", FilePath);
        }
    }

    /// <summary>Reads the file text (empty when missing).</summary>
    public string ReadText()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
            }
            catch (IOException) when (attempt < 5)
            {
                // Editors often hold the file briefly while saving.
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Loads the file. <see cref="Current"/> changes only if it has no errors.</summary>
    public ConfigLoadResult Load()
    {
        lock (_gate)
        {
            ConfigLoadResult result;
            try
            {
                result = ConfigLoader.Load(ReadText(), _scope, _catalog);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result = new ConfigLoadResult(Current, [new ConfigIssue(IssueSeverity.Error, $"Cannot read {FilePath}: {ex.Message}")]);
            }
            Apply(result);
            return result;
        }
    }

    /// <summary>
    /// Validates <paramref name="yaml"/> and, if it has no errors, writes it to the file atomically and makes it current.
    /// With errors the file is left untouched.
    /// </summary>
    public ConfigLoadResult Save(string yaml)
    {
        lock (_gate)
        {
            var result = ConfigLoader.Load(yaml, _scope, _catalog);
            if (result.HasErrors)
                return result;

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, yaml);
            File.Move(temp, FilePath, overwrite: true);
            Apply(result);
            return result;
        }
    }

    /// <summary>Starts reloading automatically when the file changes.</summary>
    public void StartWatching()
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        _debounce = new Timer(_ => SafeReload(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _debounce?.Change(TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);

    private void SafeReload()
    {
        try
        {
            Load();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reloading {Path} failed", FilePath);
        }
    }

    private void Apply(ConfigLoadResult result)
    {
        LastResult = result;
        if (!result.HasErrors)
        {
            Current = result.Config;
            _logger.LogInformation("Loaded {Path}: {Automations} automation(s), {Profiles} profile(s), {Warnings} warning(s)",
                FilePath, result.Config.Automations.Count, result.Config.Profiles.Count, result.Warnings.Count());
        }
        else
        {
            _logger.LogWarning("{Path} has {Errors} error(s); keeping the previous configuration. First error: {Error}",
                FilePath, result.Errors.Count(), result.Errors.First());
        }
        Loaded?.Invoke(this, result);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
