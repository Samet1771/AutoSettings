using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoSettings.Core.Engine;

/// <summary>How an activity entry is shown.</summary>
public enum ActivityLevel
{
    /// <summary>Something happened.</summary>
    Info,
    /// <summary>Something was done successfully.</summary>
    Success,
    /// <summary>Something was skipped or partly failed.</summary>
    Warning,
    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>Well-known values for <see cref="ActivityEntry.Source"/>.</summary>
public static class ActivitySources
{
    /// <summary>A system event (boot, logon, app ...).</summary>
    public const string Event = "event";
    /// <summary>An automation started, finished or was skipped.</summary>
    public const string Automation = "automation";
    /// <summary>An action ran.</summary>
    public const string Action = "action";
    /// <summary>A profile was applied or reverted.</summary>
    public const string Profile = "profile";
    /// <summary>Configuration was loaded.</summary>
    public const string Config = "config";
    /// <summary>Engine state (paused, resumed, loop guard).</summary>
    public const string Engine = "engine";
    /// <summary>Connection to the service / agents.</summary>
    public const string Connection = "connection";
    /// <summary>Update checks, downloads and installs.</summary>
    public const string Update = "update";
    /// <summary>Plugins: loading, errors and output.</summary>
    public const string Plugin = "plugin";
}

/// <summary>One line in the activity timeline.</summary>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Level">Severity.</param>
/// <param name="Source">One of <see cref="ActivitySources"/>.</param>
/// <param name="Message">What happened, in plain language.</param>
/// <param name="AutomationId">The automation involved, if any.</param>
public sealed record ActivityEntry(DateTimeOffset Timestamp, ActivityLevel Level, string Source, string Message, string? AutomationId = null);

/// <summary>
/// The activity timeline: a bounded, thread-safe list of what the engine did and why.
/// Every entry is also written to the regular log.
/// </summary>
public sealed class ActivityLog
{
    private readonly object _gate = new();
    private readonly Queue<ActivityEntry> _entries = new();
    private readonly int _capacity;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates a log that keeps the latest <paramref name="capacity"/> entries.</summary>
    public ActivityLog(int capacity = 1000, ILogger? logger = null, TimeProvider? time = null)
    {
        _capacity = Math.Max(10, capacity);
        _logger = logger ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised after an entry is added. May be raised on any thread.</summary>
    public event EventHandler<ActivityEntry>? EntryAdded;

    /// <summary>Adds an entry.</summary>
    public ActivityEntry Add(ActivityLevel level, string source, string message, string? automationId = null)
    {
        var entry = new ActivityEntry(_time.GetLocalNow(), level, source, message, automationId);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
                _entries.Dequeue();
        }

        var logLevel = level switch
        {
            ActivityLevel.Error => LogLevel.Error,
            ActivityLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };
        _logger.Log(logLevel, "[{Source}] {Message}", source, message);
        EntryAdded?.Invoke(this, entry);
        return entry;
    }

    /// <summary>Adds an <see cref="ActivityLevel.Info"/> entry.</summary>
    public ActivityEntry Info(string source, string message, string? automationId = null) => Add(ActivityLevel.Info, source, message, automationId);

    /// <summary>Adds a <see cref="ActivityLevel.Success"/> entry.</summary>
    public ActivityEntry Success(string source, string message, string? automationId = null) => Add(ActivityLevel.Success, source, message, automationId);

    /// <summary>Adds a <see cref="ActivityLevel.Warning"/> entry.</summary>
    public ActivityEntry Warning(string source, string message, string? automationId = null) => Add(ActivityLevel.Warning, source, message, automationId);

    /// <summary>Adds an <see cref="ActivityLevel.Error"/> entry.</summary>
    public ActivityEntry Error(string source, string message, string? automationId = null) => Add(ActivityLevel.Error, source, message, automationId);

    /// <summary>Returns the entries, oldest first.</summary>
    public IReadOnlyList<ActivityEntry> Snapshot()
    {
        lock (_gate)
            return _entries.ToList();
    }

    /// <summary>Removes all entries.</summary>
    public void Clear()
    {
        lock (_gate)
            _entries.Clear();
    }
}
