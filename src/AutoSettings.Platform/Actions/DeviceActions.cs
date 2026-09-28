using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Interop;
using Windows.Devices.Radios;

namespace AutoSettings.Platform.Actions;

/// <summary><c>radio.set</c>: Wi-Fi, Bluetooth and mobile broadband radios (Windows.Devices.Radios).</summary>
public sealed class RadioAction : IRevertibleActionHandler
{
    /// <inheritdoc />
    public string Type => "radio.set";

    /// <inheritdoc />
    public async Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        var state = action.GetString("state") ?? "toggle";
        foreach (var radio in await FindAsync(action.GetString("radio") ?? "wifi"))
        {
            var target = state switch
            {
                "on" => RadioState.On,
                "off" => RadioState.Off,
                _ => radio.State == RadioState.On ? RadioState.Off : RadioState.On,
            };
            await SetAsync(radio, target);
        }
    }

    /// <inheritdoc />
    public async Task<string?> CaptureAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        var radios = await FindAsync(action.GetString("radio") ?? "wifi");
        return radios[0].State == RadioState.On ? "on" : "off";
    }

    /// <inheritdoc />
    public async Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext context, CancellationToken cancellationToken)
    {
        var target = snapshot == "on" ? RadioState.On : RadioState.Off;
        foreach (var radio in await FindAsync(action.GetString("radio") ?? "wifi"))
            await SetAsync(radio, target);
    }

    private static async Task<List<Radio>> FindAsync(string kindName)
    {
        var access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed)
            throw new ActionFailedException("Windows denied access to the radios. Check Settings > Privacy & security > Radios.");

        var kind = kindName switch
        {
            "bluetooth" => RadioKind.Bluetooth,
            "mobile_broadband" => RadioKind.MobileBroadband,
            _ => RadioKind.WiFi,
        };
        var radios = (await Radio.GetRadiosAsync()).Where(r => r.Kind == kind).ToList();
        return radios.Count > 0 ? radios : throw new ActionFailedException($"this computer has no {kindName.Replace('_', ' ')} radio");
    }

    private static async Task SetAsync(Radio radio, RadioState state)
    {
        if (radio.State == state)
            return;
        var result = await radio.SetStateAsync(state);
        if (result != RadioAccessStatus.Allowed)
            throw new ActionFailedException($"Windows did not allow turning {radio.Name} {(state == RadioState.On ? "on" : "off")} ({result}).");
    }
}

/// <summary><c>mouse.speed</c>: pointer speed (1-20).</summary>
public sealed class MouseSpeedAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "mouse.speed";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) =>
        Set((int)(action.GetInteger("speed") ?? 10));

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var speed = 10;
        User32.SystemParametersInfo(User32.SPI_GETMOUSESPEED, 0, ref speed, 0);
        return speed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        if (int.TryParse(snapshot, System.Globalization.CultureInfo.InvariantCulture, out var speed))
            Set(speed);
    }

    private static void Set(int speed)
    {
        if (!User32.SystemParametersInfo(User32.SPI_SETMOUSESPEED, 0, new IntPtr(Math.Clamp(speed, 1, 20)), User32.SPIF_UPDATEINIFILE | User32.SPIF_SENDCHANGE))
            throw new ActionFailedException("Windows refused the mouse speed change");
    }
}
