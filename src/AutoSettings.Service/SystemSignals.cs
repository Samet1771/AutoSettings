using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoSettings.Service;

/// <summary>Relays Windows Service control events (sessions, power) to the rest of the service.</summary>
public sealed class SystemSignals
{
    /// <summary>A user session changed (logon, logoff, lock, unlock, ...).</summary>
    public event Action<SessionChangeReason, int>? SessionChanged;

    /// <summary>The power state changed (suspend, resume, ...).</summary>
    public event Action<PowerBroadcastStatus>? PowerChanged;

    internal void RaiseSession(SessionChangeReason reason, int sessionId) => SessionChanged?.Invoke(reason, sessionId);

    internal void RaisePower(PowerBroadcastStatus status) => PowerChanged?.Invoke(status);
}

/// <summary>
/// The standard Windows Service lifetime, extended to receive session-change and power notifications
/// (which is how the service learns about logon, logoff, lock, unlock and Fast Startup boots).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SessionAwareServiceLifetime : WindowsServiceLifetime
{
    private readonly SystemSignals _signals;

    public SessionAwareServiceLifetime(
        SystemSignals signals,
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        _signals = signals;
        CanHandleSessionChangeEvent = true;
        CanHandlePowerEvent = true;
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        _signals.RaiseSession(changeDescription.Reason, changeDescription.SessionId);
        base.OnSessionChange(changeDescription);
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        _signals.RaisePower(powerStatus);
        return base.OnPowerEvent(powerStatus);
    }
}
