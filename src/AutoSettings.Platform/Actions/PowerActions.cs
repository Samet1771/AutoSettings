using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Interop;

namespace AutoSettings.Platform.Actions;

/// <summary>Power scheme helpers.</summary>
internal static class PowerSchemes
{
    public static readonly IReadOnlyDictionary<string, Guid> Aliases = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
    {
        ["balanced"] = new("381b4222-f694-41f0-9685-ff5bb260df2e"),
        ["high_performance"] = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
        ["power_saver"] = new("a1841308-3541-4fab-bc81-f71556f20b4a"),
        ["ultimate_performance"] = new("e9a42b02-d5df-448d-aa00-03f14749eb61"),
    };

    public static Guid Active()
    {
        Check(PowrProf.PowerGetActiveScheme(IntPtr.Zero, out var pointer), "read the active power plan");
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            PowrProf.LocalFree(pointer);
        }
    }

    public static void Activate(Guid scheme) =>
        Check(PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref scheme), "activate the power plan");

    public static List<(Guid Id, string Name)> List()
    {
        var result = new List<(Guid, string)>();
        for (uint index = 0; ; index++)
        {
            var scheme = Guid.Empty;
            var size = (uint)Marshal.SizeOf<Guid>();
            if (PowrProf.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, PowrProf.ACCESS_SCHEME, index, ref scheme, ref size) != 0)
                break;
            result.Add((scheme, FriendlyName(scheme)));
        }
        return result;
    }

    public static Guid Resolve(string plan)
    {
        var schemes = List();
        var available = string.Join(", ", schemes.Select(s => s.Name));

        if (Aliases.TryGetValue(plan, out var alias))
        {
            if (schemes.Any(s => s.Id == alias))
                return alias;
            if (plan.Equals("ultimate_performance", StringComparison.OrdinalIgnoreCase))
            {
                var ultimate = schemes.FirstOrDefault(s => s.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
                if (ultimate.Id != Guid.Empty)
                    return ultimate.Id;
            }
            throw new ActionFailedException($"the {plan.Replace('_', ' ')} plan is not available on this computer. Available plans: {available}");
        }

        if (Guid.TryParse(plan, out var id))
        {
            return schemes.Any(s => s.Id == id)
                ? id
                : throw new ActionFailedException($"there is no power plan {id}. Available plans: {available}");
        }

        var match = schemes.FirstOrDefault(s => s.Name.Equals(plan, StringComparison.OrdinalIgnoreCase));
        if (match.Id == Guid.Empty)
            match = schemes.FirstOrDefault(s => s.Name.Contains(plan, StringComparison.OrdinalIgnoreCase));
        return match.Id != Guid.Empty
            ? match.Id
            : throw new ActionFailedException($"there is no power plan named '{plan}'. Available plans: {available}");
    }

    public static void Check(uint result, string what)
    {
        if (result != 0)
            throw new Win32Exception((int)result, $"Could not {what}");
    }

    private static string FriendlyName(Guid scheme)
    {
        uint size = 0;
        PowrProf.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (size == 0)
            return scheme.ToString();
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowrProf.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer) ?? scheme.ToString()
                : scheme.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

/// <summary><c>power.plan</c>: activates a power plan.</summary>
public sealed class PowerPlanAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "power.plan";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) =>
        PowerSchemes.Activate(PowerSchemes.Resolve(action.GetString("plan") ?? "balanced"));

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) => PowerSchemes.Active().ToString();

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        if (Guid.TryParse(snapshot, out var scheme))
            PowerSchemes.Activate(scheme);
    }
}

/// <summary><c>power.screen_timeout</c> and <c>power.sleep_timeout</c>: idle timeouts on the active plan.</summary>
public sealed class PowerTimeoutAction : SyncRevertibleActionHandler
{
    private readonly string _type;
    private readonly Guid _subgroup;
    private readonly Guid _setting;

    private PowerTimeoutAction(string type, Guid subgroup, Guid setting)
    {
        _type = type;
        _subgroup = subgroup;
        _setting = setting;
    }

    /// <summary>Screen turn-off timeout.</summary>
    public static PowerTimeoutAction Screen() => new("power.screen_timeout", PowrProf.VideoSubgroup, PowrProf.VideoIdle);

    /// <summary>Sleep timeout.</summary>
    public static PowerTimeoutAction Sleep() => new("power.sleep_timeout", PowrProf.SleepSubgroup, PowrProf.StandbyIdle);

    /// <inheritdoc />
    public override string Type => _type;

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var seconds = (uint)Math.Max(0, (action.GetInteger("minutes") ?? 0) * 60);
        var source = action.GetString("power_source") ?? "both";
        Write(source is "both" or "ac" ? seconds : null, source is "both" or "battery" ? seconds : null);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var scheme = PowerSchemes.Active();
        var subgroup = _subgroup;
        var setting = _setting;
        PowerSchemes.Check(PowrProf.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var ac), "read the timeout");
        PowerSchemes.Check(PowrProf.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var dc), "read the timeout");
        return string.Create(CultureInfo.InvariantCulture, $"{ac},{dc}");
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var parts = (snapshot ?? "").Split(',');
        if (parts.Length != 2)
            return;
        var source = action.GetString("power_source") ?? "both";
        Write(
            source is "both" or "ac" ? uint.Parse(parts[0], CultureInfo.InvariantCulture) : null,
            source is "both" or "battery" ? uint.Parse(parts[1], CultureInfo.InvariantCulture) : null);
    }

    private void Write(uint? ac, uint? dc)
    {
        var scheme = PowerSchemes.Active();
        var subgroup = _subgroup;
        var setting = _setting;
        if (ac is { } acValue)
            PowerSchemes.Check(PowrProf.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, acValue), "change the timeout");
        if (dc is { } dcValue)
            PowerSchemes.Check(PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, dcValue), "change the timeout");
        PowerSchemes.Activate(scheme);
    }
}
