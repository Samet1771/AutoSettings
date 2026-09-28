namespace AutoSettings.Core.Events;

/// <summary>Something that watches the system and raises <see cref="SystemEvent"/>s (process monitor, focus monitor, ...).</summary>
public interface ISystemEventSource : IDisposable
{
    /// <summary>Human-readable name used in logs, e.g. "ETW process monitor".</summary>
    string Name { get; }

    /// <summary>Raised for every event. May be raised on any thread.</summary>
    event EventHandler<SystemEvent>? EventRaised;

    /// <summary>Starts watching. Throws if the source cannot work on this machine.</summary>
    void Start();
}
