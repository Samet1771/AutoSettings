using System.Collections.Concurrent;
using System.Management;
using AutoSettings.Core.Events;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace AutoSettings.Platform.Monitoring;

/// <summary>A source of app_started / app_closed events that also knows which processes are running.</summary>
public interface IProcessMonitor : ISystemEventSource
{
    /// <summary>Processes running right now (session, process).</summary>
    IReadOnlyList<(int SessionId, ProcessInfo Process)> RunningProcesses { get; }
}

/// <summary>Shared bookkeeping for process monitors.</summary>
public abstract class ProcessMonitorBase : IProcessMonitor
{
    private readonly ConcurrentDictionary<int, (int SessionId, ProcessInfo Process)> _known = new();

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public event EventHandler<SystemEvent>? EventRaised;

    /// <inheritdoc />
    public IReadOnlyList<(int SessionId, ProcessInfo Process)> RunningProcesses => _known.Values.ToList();

    /// <summary>Restricts events to one session (agents); null for all sessions (service).</summary>
    protected int? SessionFilter { get; init; }

    /// <inheritdoc />
    public void Start()
    {
        foreach (var entry in ProcessQuery.Snapshot(SessionFilter))
            _known[entry.Process.Id] = entry;
        StartCore();
    }

    /// <summary>Starts the underlying mechanism.</summary>
    protected abstract void StartCore();

    /// <summary>Records a started process and raises app_started.</summary>
    protected void OnStarted(int processId, int? sessionId, string? imageName)
    {
        var session = sessionId ?? ProcessQuery.TryGetSessionId(processId) ?? -1;
        if (SessionFilter is not null && session != SessionFilter)
            return;
        var path = DevicePaths.ToDosPath(imageName) ?? ProcessQuery.TryGetPath(processId);
        var name = Path.GetFileName(path ?? imageName ?? "");
        if (string.IsNullOrEmpty(name))
            return;
        var info = new ProcessInfo(processId, name, path);
        _known[processId] = (session, info);
        EventRaised?.Invoke(this, new SystemEvent { Kind = SystemEventKind.AppStarted, Process = info, SessionId = session });
    }

    /// <summary>Forgets a process and raises app_closed.</summary>
    protected void OnStopped(int processId, string? imageName)
    {
        if (_known.TryRemove(processId, out var entry))
        {
            EventRaised?.Invoke(this, new SystemEvent { Kind = SystemEventKind.AppClosed, Process = entry.Process, SessionId = entry.SessionId });
            return;
        }
        // Unknown process (started before we did and missed by the snapshot): report what we know.
        if (SessionFilter is null && !string.IsNullOrEmpty(imageName))
        {
            var path = DevicePaths.ToDosPath(imageName);
            var info = new ProcessInfo(processId, Path.GetFileName(path ?? imageName), path);
            EventRaised?.Invoke(this, new SystemEvent { Kind = SystemEventKind.AppClosed, Process = info });
        }
    }

    /// <summary>Known process ids.</summary>
    protected ICollection<int> KnownIds => _known.Keys;

    /// <inheritdoc />
    public virtual void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>
/// Instant process start/stop notifications from Event Tracing for Windows
/// (Microsoft-Windows-Kernel-Process provider). Requires administrator rights.
/// </summary>
public sealed class EtwProcessMonitor : ProcessMonitorBase
{
    private const string SessionName = "AutoSettings-ProcessMonitor";
    private const string ProviderName = "Microsoft-Windows-Kernel-Process";
    private const ulong ProcessKeyword = 0x10;
    private readonly ILogger? _logger;
    private TraceEventSession? _session;
    private Thread? _thread;

    /// <summary>Creates the monitor.</summary>
    public EtwProcessMonitor(ILogger? logger = null) => _logger = logger;

    /// <inheritdoc />
    public override string Name => "event tracing (ETW)";

    /// <inheritdoc />
    protected override void StartCore()
    {
        if (TraceEventSession.IsElevated() != true)
            throw new InvalidOperationException("Event tracing requires administrator rights.");

        _session = new TraceEventSession(SessionName) { StopOnDispose = true };
        _session.EnableProvider(ProviderName, TraceEventLevel.Informational, ProcessKeyword);
        var parser = new RegisteredTraceEventParser(_session.Source);
        parser.All += OnEvent;

        _thread = new Thread(() =>
        {
            try
            {
                _session.Source.Process();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ETW processing stopped");
            }
        })
        {
            IsBackground = true,
            Name = "ETW process monitor",
        };
        _thread.Start();
    }

    private void OnEvent(TraceEvent data)
    {
        try
        {
            switch ((int)data.ID)
            {
                case 1: // ProcessStart
                    OnStarted(
                        Convert.ToInt32(data.PayloadByName("ProcessID")),
                        Convert.ToInt32(data.PayloadByName("SessionID")),
                        data.PayloadByName("ImageName") as string);
                    break;
                case 2: // ProcessStop
                    OnStopped(Convert.ToInt32(data.PayloadByName("ProcessID")), data.PayloadByName("ImageName") as string);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not parse ETW event {Id}", data.ID);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _session?.Dispose();
        base.Dispose();
    }
}

/// <summary>Process notifications from WMI (Win32_ProcessStartTrace / StopTrace). Requires administrator rights.</summary>
public sealed class WmiProcessMonitor : ProcessMonitorBase
{
    private ManagementEventWatcher? _start;
    private ManagementEventWatcher? _stop;

    /// <inheritdoc />
    public override string Name => "WMI";

    /// <inheritdoc />
    protected override void StartCore()
    {
        _start = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID, SessionID, ProcessName FROM Win32_ProcessStartTrace"));
        _start.EventArrived += (_, e) => OnStarted(
            Convert.ToInt32(e.NewEvent["ProcessID"]),
            Convert.ToInt32(e.NewEvent["SessionID"]),
            e.NewEvent["ProcessName"] as string);
        _start.Start();

        _stop = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID, ProcessName FROM Win32_ProcessStopTrace"));
        _stop.EventArrived += (_, e) => OnStopped(Convert.ToInt32(e.NewEvent["ProcessID"]), e.NewEvent["ProcessName"] as string);
        _stop.Start();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _start?.Stop();
        _start?.Dispose();
        _stop?.Stop();
        _stop?.Dispose();
        base.Dispose();
    }
}

/// <summary>Detects process starts and exits by comparing the process list periodically. Works without admin rights.</summary>
public sealed class PollingProcessMonitor : ProcessMonitorBase
{
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private Timer? _timer;

    /// <summary>Creates a monitor for one session (or all sessions when null).</summary>
    public PollingProcessMonitor(int? sessionId, TimeSpan? interval = null)
    {
        SessionFilter = sessionId;
        _interval = interval ?? TimeSpan.FromSeconds(2);
    }

    /// <inheritdoc />
    public override string Name => $"process list polling (every {_interval.TotalSeconds:0.#}s)";

    /// <inheritdoc />
    protected override void StartCore() => _timer = new Timer(_ => Poll(), null, _interval, _interval);

    private void Poll()
    {
        if (!Monitor.TryEnter(_gate))
            return;
        try
        {
            var current = System.Diagnostics.Process.GetProcesses();
            var seen = new HashSet<int>();
            foreach (var process in current)
            {
                using (process)
                {
                    try
                    {
                        if (SessionFilter is not null && process.SessionId != SessionFilter)
                            continue;
                        seen.Add(process.Id);
                        if (!KnownIds.Contains(process.Id) && process.Id is not (0 or 4))
                            OnStarted(process.Id, process.SessionId, null);
                    }
                    catch (InvalidOperationException)
                    {
                        // Exited while enumerating.
                    }
                }
            }
            foreach (var id in KnownIds.Where(id => !seen.Contains(id)).ToList())
                OnStopped(id, null);
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }
}

/// <summary>Picks the best process monitor that works in the current context.</summary>
public static class ProcessMonitorFactory
{
    /// <summary>Tries ETW, then WMI, then polling. Returns a started monitor.</summary>
    public static IProcessMonitor CreateAndStart(ILogger logger)
    {
        Func<IProcessMonitor>[] candidates = [() => new EtwProcessMonitor(logger), () => new WmiProcessMonitor(), () => new PollingProcessMonitor(null)];
        foreach (var create in candidates)
        {
            var monitor = create();
            try
            {
                monitor.Start();
                logger.LogInformation("Process monitor: {Name}", monitor.Name);
                return monitor;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Process monitor {Name} is not available", monitor.Name);
                monitor.Dispose();
            }
        }
        throw new InvalidOperationException("No process monitor could be started.");
    }
}
