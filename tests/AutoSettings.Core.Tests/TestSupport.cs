using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Tests;

/// <summary>A clock tests can move forward by hand.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTime(DateTimeOffset? start = null) => _now = start ?? new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset now) => _now = now;
}

/// <summary>Records every call.</summary>
internal sealed class RecordingAction(string type) : IActionHandler
{
    public string Type => type;

    public List<ComponentConfig> Calls { get; } = [];

    public Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        lock (Calls)
            Calls.Add(action);
        return Task.CompletedTask;
    }
}

/// <summary>Always fails.</summary>
internal sealed class FailingAction(string type) : IActionHandler
{
    public string Type => type;

    public Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken) =>
        throw new ActionFailedException("boom");
}

/// <summary>An in-memory, revertible volume setting.</summary>
internal sealed class FakeVolume : IRevertibleActionHandler
{
    public string Type => "audio.volume";

    public long Level { get; set; } = 50;

    public List<long> History { get; } = [];

    public Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        Level = action.GetInteger("level")!.Value;
        History.Add(Level);
        return Task.CompletedTask;
    }

    public Task<string?> CaptureAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(Level.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext context, CancellationToken cancellationToken)
    {
        Level = long.Parse(snapshot!, System.Globalization.CultureInfo.InvariantCulture);
        History.Add(Level);
        return Task.CompletedTask;
    }
}

internal static class TestSupport
{
    public static readonly UserInfo Samet = new("samet", "PC", "S-1-5-21-1-2-3-1001");

    public static AutomationConfig LoadValid(string yaml, ExecutionScope scope = ExecutionScope.User)
    {
        var result = ConfigLoader.Load(yaml, scope);
        Assert.False(result.HasErrors, "Unexpected errors:\n" + string.Join("\n", result.Errors));
        return result.Config;
    }

    public static RuleEngine CreateEngine(string yaml, HandlerRegistry handlers, ManualTime? time = null, EngineOptions? options = null,
        ExecutionScope scope = ExecutionScope.User)
    {
        var engine = new RuleEngine(scope, handlers, new ActivityLog(), options, time: time ?? new ManualTime())
        {
            CurrentUser = Samet,
        };
        engine.UpdateConfig(LoadValid(yaml, scope));
        return engine;
    }

    public static async Task SendAsync(this RuleEngine engine, params SystemEvent[] events)
    {
        foreach (var e in events)
        {
            await engine.HandleEventAsync(e);
            await engine.WhenIdleAsync();
        }
    }

    public static SystemEvent App(SystemEventKind kind, string exe, int pid = 100, int session = 1, string? title = null) => new()
    {
        Kind = kind,
        Process = new ProcessInfo(pid, exe, $@"C:\Program Files\{Path.GetFileNameWithoutExtension(exe)}\{exe}"),
        SessionId = session,
        WindowTitle = title,
    };

    public static SystemEvent Session(SystemEventKind kind, UserInfo? user = null, int session = 1) => new()
    {
        Kind = kind,
        User = user ?? Samet,
        SessionId = session,
    };

    public static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AutoSettings.slnx")))
            directory = directory.Parent;
        return directory?.FullName;
    }
}
