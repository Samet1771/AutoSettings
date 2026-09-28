using AutoSettings.Core.Events;

namespace AutoSettings.Core.Engine;

/// <summary>Extra facts about an event, computed from engine state.</summary>
/// <param name="IsFirstInstance">For app_started: no other process with the same exe was running in the session.</param>
/// <param name="IsLastInstance">For app_closed: no other process with the same exe is still running in the session.</param>
public readonly record struct EventFacts(bool IsFirstInstance, bool IsLastInstance);

/// <summary>
/// Tracks running processes from app_started/app_closed events so the engine can tell the first
/// and last instance of an app apart, and answer <c>app_running</c> conditions.
/// </summary>
public sealed class ProcessTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (int Session, ProcessInfo Process)> _processes = new();

    /// <summary>Replaces the known process list, e.g. with a snapshot taken at startup.</summary>
    public void Seed(IEnumerable<(int SessionId, ProcessInfo Process)> processes)
    {
        lock (_gate)
        {
            _processes.Clear();
            foreach (var (session, process) in processes)
                _processes[process.Id] = (session, process);
        }
    }

    /// <summary>Updates the list for an event and returns first/last-instance facts.</summary>
    public EventFacts Observe(SystemEvent e)
    {
        if (e.Process is not { } process)
            return default;
        var session = e.SessionId ?? -1;

        lock (_gate)
        {
            switch (e.Kind)
            {
                case SystemEventKind.AppStarted:
                {
                    var first = !_processes.Any(p => p.Key != process.Id && SameApp(p.Value, session, process));
                    _processes[process.Id] = (session, process);
                    return new EventFacts(first, false);
                }
                case SystemEventKind.AppClosed:
                {
                    _processes.Remove(process.Id);
                    var last = !_processes.Values.Any(p => SameApp(p, session, process));
                    return new EventFacts(false, last);
                }
                default:
                    return default;
            }
        }
    }

    /// <summary>Whether a process matching any of <paramref name="patterns"/> is running.</summary>
    public bool IsRunning(IReadOnlyList<string> patterns)
    {
        lock (_gate)
            return _processes.Values.Any(p => AppPattern.MatchesAny(patterns, p.Process));
    }

    /// <summary>Number of tracked processes.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _processes.Count;
        }
    }

    private static bool SameApp((int Session, ProcessInfo Process) candidate, int session, ProcessInfo process) =>
        (candidate.Session == session || candidate.Session == -1 || session == -1)
        && string.Equals(candidate.Process.Name, process.Name, StringComparison.OrdinalIgnoreCase);
}
