using System.Text;
using AutoSettings.Core.Events;
using AutoSettings.Platform.Interop;

namespace AutoSettings.Platform.Monitoring;

/// <summary>
/// Raises app_focused / app_unfocused when the foreground window moves to a different app.
/// Must be started on a thread that runs a message loop (the agent's UI thread).
/// </summary>
public sealed class ForegroundMonitor : ISystemEventSource
{
    private readonly User32.WinEventDelegate _callback;
    private readonly Timer _debounce;
    private readonly TimeSpan _delay;
    private readonly object _gate = new();
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly int _sessionId = Sessions.CurrentSessionId();
    private IntPtr _hook;
    private ProcessInfo? _current;
    private string? _currentTitle;

    /// <summary>Creates the monitor. Focus must be stable for <paramref name="debounce"/> (default 250 ms) before events are raised.</summary>
    public ForegroundMonitor(TimeSpan? debounce = null)
    {
        _callback = OnWinEvent;
        _delay = debounce ?? TimeSpan.FromMilliseconds(250);
        _debounce = new Timer(_ => Evaluate(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public string Name => "foreground window hook";

    /// <inheritdoc />
    public event EventHandler<SystemEvent>? EventRaised;

    /// <summary>The app that currently has focus.</summary>
    public ProcessInfo? CurrentApp
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    /// <summary>Title of the focused window.</summary>
    public string? CurrentTitle
    {
        get
        {
            lock (_gate)
                return _currentTitle;
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        _hook = User32.SetWinEventHook(User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, User32.WINEVENT_OUTOFCONTEXT);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Could not install the foreground window hook.");
        _debounce.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Whether a full-screen app, game or presentation is in the foreground.</summary>
    public static bool IsFullScreenAppActive()
    {
        if (User32.SHQueryUserNotificationState(out var state) != 0)
            return false;
        return state is User32.UserNotificationState.Busy
            or User32.UserNotificationState.RunningDirect3DFullScreen
            or User32.UserNotificationState.PresentationMode;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime) =>
        _debounce.Change(_delay, Timeout.InfiniteTimeSpan);

    private void Evaluate()
    {
        try
        {
            var hwnd = User32.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return;
            User32.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0 || processId == _ownProcessId)
                return;

            var app = ProcessQuery.TryDescribe(processId);
            if (app is null)
                return;
            var title = WindowTitle(hwnd);

            ProcessInfo? previous;
            string? previousTitle;
            lock (_gate)
            {
                if (_current is not null && SameApp(_current, app))
                {
                    _currentTitle = title;
                    return;
                }
                previous = _current;
                previousTitle = _currentTitle;
                _current = app;
                _currentTitle = title;
            }

            if (previous is not null)
                Raise(SystemEventKind.AppUnfocused, previous, previousTitle);
            Raise(SystemEventKind.AppFocused, app, title);
        }
        catch (Exception)
        {
            // Never let a transient Win32 failure kill the timer thread.
        }
    }

    private void Raise(SystemEventKind kind, ProcessInfo app, string? title) =>
        EventRaised?.Invoke(this, new SystemEvent { Kind = kind, Process = app, WindowTitle = title, SessionId = _sessionId });

    private static bool SameApp(ProcessInfo a, ProcessInfo b) =>
        a.Path is not null && b.Path is not null
            ? string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase)
            : string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    private static string WindowTitle(IntPtr hwnd)
    {
        var length = User32.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";
        var buffer = new StringBuilder(length + 1);
        User32.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            User32.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _debounce.Dispose();
    }
}
