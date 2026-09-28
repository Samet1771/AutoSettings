using System.IO;
using System.Windows;
using System.Windows.Threading;
using AutoSettings.Agent.Localization;
using AutoSettings.Core;
using AutoSettings.Core.Updates;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Appearance;
using Serilog;
using Serilog.Extensions.Logging;

namespace AutoSettings.Agent;

/// <summary>
/// Entry point. Runs one agent per user session: a tray icon plus the personal automation engine.
/// Command line: <c>--background</c> starts in the tray without opening the window.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\AutoSettings.Agent";
    private const string ShowSignalName = @"Local\AutoSettings.Agent.Show";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private SerilogLoggerFactory? _loggerFactory;
    private TrayIcon? _tray;
    private AgentHost? _host;
    private MainWindow? _window;
    private AgentSettings _settings = new();
    private bool _exiting;

    /// <summary>Applies "system", "light" or "dark".</summary>
    public static void ApplyTheme(string theme)
    {
        switch (theme)
        {
            case "light":
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case "dark":
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running in this session: ask it to show its window and quit.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            Shutdown();
            return;
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(Product.AgentLogDirectory, "agent-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        _loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var logger = _loggerFactory.CreateLogger<App>();

        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception");

        _settings = AgentSettings.Load();
        Strings.Use(_settings.Language);
        ApplyTheme(_settings.Theme);

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.InvokeAsync(ShowMainWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _tray = new TrayIcon(Dispatcher);
        _host = new AgentHost(_tray, _loggerFactory);
        _tray.Attach(_host, ShowMainWindow, () => _ = ExitAsync());

        try
        {
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "The agent could not start");
            MessageBox.Show(Strings.Format("CouldNotStart", Product.Name, ex.Message), Product.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _host.UpdateStatusChanged += (_, status) => Dispatcher.InvokeAsync(() => OnUpdateStatus(status));
        AnnounceCompletedUpdate();

        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            ShowMainWindow();
    }

    /// <summary>Says "AutoSettings was updated" the first time a new version runs.</summary>
    private void AnnounceCompletedUpdate()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var previous = _settings.LastRunVersion;
        if (previous == version)
            return;
        _settings.LastRunVersion = version;
        TrySaveSettings();
        if (previous is not null)
            _tray?.Notify(Strings.Get("UpdatedTitle"), Strings.Format("UpdatedMessage", version), ShowUpdateSettings);
    }

    private void OnUpdateStatus(UpdateStatus status)
    {
        if (status.State == UpdateState.Installing)
        {
            // The installer replaces this program's files; the new version is started afterwards.
            _ = ExitForUpdateAsync();
            return;
        }

        // Announce each new version once, unless it is installed automatically anyway.
        if (status.Latest is not { } release
            || status.State is not (UpdateState.Available or UpdateState.Ready)
            || status.Settings.Mode is UpdateMode.Off
            || status.Settings.Mode is UpdateMode.Automatic && status.CanInstall
            || _settings.LastNotifiedUpdate == release.Version)
            return;

        _settings.LastNotifiedUpdate = release.Version;
        TrySaveSettings();
        var message = status.CanInstall ? Strings.Get("UpdateAvailableAsk") : Strings.Get("UpdateAvailableNotify");
        _tray?.Notify(Strings.Format("UpdateAvailableTitle", release.Version), message, ShowUpdateSettings);
    }

    private void ShowUpdateSettings()
    {
        ShowMainWindow();
        _window?.ShowSettings();
    }

    private void TrySaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task ExitForUpdateAsync()
    {
        if (_exiting)
            return;
        _exiting = true;
        _window?.Close();
        if (_host is not null)
            await _host.StopAsync(userInitiated: false);
        _tray?.Dispose();
        // Exit code 0: the service does not restart the agent; the updated service starts the new one.
        Shutdown(0);
    }

    private void ShowMainWindow()
    {
        if (_host is null || _exiting)
            return;
        if (_window is null)
        {
            _window = new MainWindow(_host, _settings);
            _window.Closed += (_, _) => _window = null;
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async Task ExitAsync()
    {
        if (_exiting)
            return;
        _exiting = true;
        _window?.Close();
        if (_host is not null)
            await _host.StopAsync(userInitiated: true);
        _tray?.Dispose();
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exiting && _host is not null)
        {
            // Windows is signing out or shutting down.
            _host.StopAsync(userInitiated: false).Wait(TimeSpan.FromSeconds(5));
        }
        _tray?.Dispose();
        _showSignal?.Dispose();
        _singleInstance?.Dispose();
        _loggerFactory?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
