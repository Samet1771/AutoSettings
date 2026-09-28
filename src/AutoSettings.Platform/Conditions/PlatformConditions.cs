using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Interop;
using AutoSettings.Platform.Monitoring;
using Windows.Networking.Connectivity;

namespace AutoSettings.Platform.Conditions;

/// <summary>Base class for conditions evaluated synchronously.</summary>
public abstract class SyncConditionHandler : IConditionHandler
{
    /// <inheritdoc />
    public abstract string Type { get; }

    /// <inheritdoc />
    public ValueTask<bool> EvaluateAsync(ComponentConfig condition, ConditionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Evaluate(condition, context));

    /// <summary>Evaluates the condition.</summary>
    protected abstract bool Evaluate(ComponentConfig condition, ConditionContext context);
}

/// <summary>Reads the power status.</summary>
internal static class PowerStatus
{
    private const byte NoSystemBattery = 128;

    public static bool OnBattery()
    {
        if (!Native.GetSystemPowerStatus(out var status))
            return false;
        return status.ACLineStatus == 0 && (status.BatteryFlag & NoSystemBattery) == 0;
    }

    public static int? BatteryPercent()
    {
        if (!Native.GetSystemPowerStatus(out var status))
            return null;
        if ((status.BatteryFlag & NoSystemBattery) != 0 || status.BatteryFlag == 255 || status.BatteryLifePercent > 100)
            return null;
        return status.BatteryLifePercent;
    }
}

/// <summary><c>power_source</c>.</summary>
public sealed class PowerSourceCondition : SyncConditionHandler
{
    /// <inheritdoc />
    public override string Type => "power_source";

    /// <inheritdoc />
    protected override bool Evaluate(ComponentConfig condition, ConditionContext context) =>
        PowerStatus.OnBattery() == (condition.GetString("is") == "battery");
}

/// <summary><c>battery</c>.</summary>
public sealed class BatteryCondition : SyncConditionHandler
{
    /// <inheritdoc />
    public override string Type => "battery";

    /// <inheritdoc />
    protected override bool Evaluate(ComponentConfig condition, ConditionContext context)
    {
        if (PowerStatus.BatteryPercent() is not { } percent)
            return false;
        if (condition.GetInteger("above") is { } above && percent <= above)
            return false;
        if (condition.GetInteger("below") is { } below && percent >= below)
            return false;
        return true;
    }
}

/// <summary><c>wifi</c>: connected Wi-Fi network name.</summary>
public sealed class WifiCondition : SyncConditionHandler
{
    /// <inheritdoc />
    public override string Type => "wifi";

    /// <inheritdoc />
    protected override bool Evaluate(ComponentConfig condition, ConditionContext context)
    {
        var patterns = condition.GetStringList("ssid");
        var connected = ConnectedNetworks().Any(ssid => patterns.Any(p => Wildcard.IsMatch(p, ssid)));
        return connected == (condition.GetBoolean("connected") ?? true);
    }

    /// <summary>Names of the Wi-Fi networks the computer is connected to.</summary>
    public static IReadOnlyList<string> ConnectedNetworks()
    {
        var result = new List<string>();
        foreach (var profile in NetworkInformation.GetConnectionProfiles())
        {
            if (!profile.IsWlanConnectionProfile)
                continue;
            var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
            if (!string.IsNullOrEmpty(ssid))
                result.Add(ssid);
        }
        return result;
    }
}

/// <summary><c>monitor_count</c>.</summary>
public sealed class MonitorCountCondition : SyncConditionHandler
{
    /// <inheritdoc />
    public override string Type => "monitor_count";

    /// <inheritdoc />
    protected override bool Evaluate(ComponentConfig condition, ConditionContext context)
    {
        var count = User32.GetSystemMetrics(User32.SM_CMONITORS);
        if (condition.GetInteger("min") is { } min && count < min)
            return false;
        if (condition.GetInteger("max") is { } max && count > max)
            return false;
        return true;
    }
}

/// <summary><c>app_focused</c> (condition).</summary>
public sealed class AppFocusedCondition(ForegroundMonitor foreground) : SyncConditionHandler
{
    /// <inheritdoc />
    public override string Type => "app_focused";

    /// <inheritdoc />
    protected override bool Evaluate(ComponentConfig condition, ConditionContext context)
    {
        var app = context.Event?.Kind == Core.Events.SystemEventKind.AppFocused ? context.Event.Process : foreground.CurrentApp;
        return AppPattern.MatchesAny(condition.GetStringList("app"), app);
    }
}

/// <summary><c>fullscreen</c>.</summary>
public sealed class FullscreenCondition : SyncConditionHandler
{
    /// <inheritdoc />
    public override string Type => "fullscreen";

    /// <inheritdoc />
    protected override bool Evaluate(ComponentConfig condition, ConditionContext context) =>
        ForegroundMonitor.IsFullScreenAppActive() == (condition.GetBoolean("active") ?? true);
}
