using AutoSettings.Core.Engine;
using AutoSettings.Platform.Actions;
using AutoSettings.Platform.Conditions;
using AutoSettings.Platform.Monitoring;

namespace AutoSettings.Platform;

/// <summary>Creates the handler sets for the agent (user) and the service (machine).</summary>
public static class PlatformHandlers
{
    /// <summary>Actions that run in the signed-in user's session.</summary>
    public static IEnumerable<IActionHandler> UserActions(INotifier notifier) =>
    [
        new ThemeModeAction(),
        new TransparencyAction(),
        new WallpaperAction(),
        new TaskbarAutoHideAction(),
        new BrightnessAction(),
        new ResolutionAction(),
        new PowerPlanAction(),
        PowerTimeoutAction.Screen(),
        PowerTimeoutAction.Sleep(),
        new VolumeAction(),
        new MuteAction(),
        new DefaultAudioDeviceAction(),
        new RadioAction(),
        new MouseSpeedAction(),
        new AppLaunchAction(),
        new AppCloseAction(),
        new CommandRunAction(),
        new NotifyAction(notifier),
        new OpenAction(),
        new RegistrySetAction(),
        new AccentColorAction(),
        new HdrAction(),
        new ScalingAction(),
        new PrimaryMonitorAction(),
        new NightLightAction(),
        new PowerModeAction(),
        new NotificationBannersAction(),
        new DoNotDisturbAction(),
        new KeyboardLayoutAction(),
        new AirplaneModeAction(),
        new SettingsOpenAction(),
    ];

    /// <summary>Actions the service runs itself, as SYSTEM.</summary>
    public static IEnumerable<IActionHandler> MachineActions() =>
    [
        new RegistrySetAction(),
        new ServiceControlAction(),
        new CommandRunAction(),
    ];

    /// <summary>Conditions that work in both the agent and the service.</summary>
    public static IEnumerable<IConditionHandler> CommonConditions() =>
    [
        new PowerSourceCondition(),
        new BatteryCondition(),
        new WifiCondition(),
    ];

    /// <summary>Conditions that need the user's desktop.</summary>
    public static IEnumerable<IConditionHandler> UserConditions(ForegroundMonitor foreground) =>
    [
        new AppFocusedCondition(foreground),
        new FullscreenCondition(),
        new MonitorCountCondition(),
    ];

    /// <summary>The complete handler registry for an agent.</summary>
    public static HandlerRegistry CreateUserRegistry(INotifier notifier, ForegroundMonitor foreground)
    {
        var registry = new HandlerRegistry();
        foreach (var handler in UserActions(notifier))
            registry.Add(handler);
        foreach (var handler in CommonConditions().Concat(UserConditions(foreground)))
            registry.Add(handler);
        return registry;
    }
}
