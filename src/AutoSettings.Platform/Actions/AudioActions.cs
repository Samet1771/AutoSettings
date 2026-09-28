using System.Globalization;
using System.Runtime.InteropServices;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Interop;
using AutoSettings.Core.Engine;

namespace AutoSettings.Platform.Actions;

/// <summary><c>audio.volume</c>: master volume of a playback device.</summary>
public sealed class VolumeAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "audio.volume";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var device = CoreAudio.Find(EDataFlow.Render, action.GetString("device"));
        SetLevel(device.Id, (int)(action.GetInteger("level") ?? 0));
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var device = CoreAudio.Find(EDataFlow.Render, action.GetString("device"));
        var volume = CoreAudio.Volume(device);
        Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
        return $"{device.Id}|{((int)Math.Round(level * 100)).ToString(CultureInfo.InvariantCulture)}";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var parts = (snapshot ?? "").Split('|');
        if (parts.Length == 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var level))
            SetLevel(parts[0], level);
    }

    private static void SetLevel(string deviceId, int level)
    {
        var volume = CoreAudio.Volume(new AudioDevice(deviceId, deviceId));
        var eventContext = Guid.Empty;
        Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 100) / 100f, ref eventContext));
    }
}

/// <summary><c>audio.mute</c>: mute state of a playback device.</summary>
public sealed class MuteAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "audio.mute";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var device = CoreAudio.Find(EDataFlow.Render, action.GetString("device"));
        var volume = CoreAudio.Volume(device);
        var mute = action.GetString("state") switch
        {
            "mute" => true,
            "unmute" => false,
            _ => !IsMuted(volume),
        };
        SetMute(volume, mute);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var device = CoreAudio.Find(EDataFlow.Render, action.GetString("device"));
        return $"{device.Id}|{(IsMuted(CoreAudio.Volume(device)) ? 1 : 0)}";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var parts = (snapshot ?? "").Split('|');
        if (parts.Length == 2)
            SetMute(CoreAudio.Volume(new AudioDevice(parts[0], parts[0])), parts[1] == "1");
    }

    private static bool IsMuted(IAudioEndpointVolume volume)
    {
        Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
        return muted;
    }

    private static void SetMute(IAudioEndpointVolume volume, bool mute)
    {
        var eventContext = Guid.Empty;
        Marshal.ThrowExceptionForHR(volume.SetMute(mute, ref eventContext));
    }
}

/// <summary><c>audio.default_device</c>: default playback or recording device.</summary>
public sealed class DefaultAudioDeviceAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "audio.default_device";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var flow = Flow(action);
        var device = CoreAudio.Find(flow, action.GetString("device"));
        foreach (var role in Roles(action))
            CoreAudio.SetDefault(device.Id, role);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var flow = Flow(action);
        return string.Join("|", Roles(action).Select(role => $"{(int)role}={CoreAudio.Default(flow, role)?.Id}"));
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        foreach (var part in (snapshot ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[1].Length > 0 && int.TryParse(pair[0], CultureInfo.InvariantCulture, out var role))
                CoreAudio.SetDefault(pair[1], (ERole)role);
        }
    }

    private static EDataFlow Flow(ComponentConfig action) =>
        action.GetString("flow") == "input" ? EDataFlow.Capture : EDataFlow.Render;

    private static ERole[] Roles(ComponentConfig action) => action.GetString("role") switch
    {
        "multimedia" => [ERole.Console, ERole.Multimedia],
        "communications" => [ERole.Communications],
        _ => [ERole.Console, ERole.Multimedia, ERole.Communications],
    };
}
