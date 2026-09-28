using System.Globalization;
using System.ServiceProcess;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Interop;
using Microsoft.Win32;

namespace AutoSettings.Platform.Actions;

/// <summary><c>registry.set</c>: writes a registry value.</summary>
public sealed class RegistrySetAction : SyncRevertibleActionHandler
{
    private const string Absent = "-";
    private const char ListSeparator = '\u001F';

    /// <inheritdoc />
    public override string Type => "registry.set";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var (root, subKey) = Open(action);
        var name = action.GetString("name") ?? "";
        var kind = Kind(action.GetString("kind"));
        using (var key = root.CreateSubKey(subKey, writable: true))
            key.SetValue(name, Convert(action.GetString("value") ?? "", kind), kind);
        if (action.GetBoolean("broadcast") == true)
            User32.BroadcastSettingChange(null);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var (root, subKey) = Open(action);
        var name = action.GetString("name") ?? "";
        using var key = root.OpenSubKey(subKey, writable: false);
        if (key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value)
            return Absent;
        var kind = key.GetValueKind(name);
        var payload = value switch
        {
            string s => s,
            string[] items => string.Join(ListSeparator, items),
            byte[] bytes => System.Convert.ToBase64String(bytes),
            int i => unchecked((uint)i).ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        return $"{(int)kind}:{payload}";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var (root, subKey) = Open(action);
        var name = action.GetString("name") ?? "";
        if (snapshot is null or Absent)
        {
            using var existing = root.OpenSubKey(subKey, writable: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            var separator = snapshot.IndexOf(':');
            var kind = (RegistryValueKind)int.Parse(snapshot[..separator], CultureInfo.InvariantCulture);
            var payload = snapshot[(separator + 1)..];
            object value = kind switch
            {
                RegistryValueKind.MultiString => payload.Split(ListSeparator),
                RegistryValueKind.Binary => System.Convert.FromBase64String(payload),
                RegistryValueKind.DWord => unchecked((int)uint.Parse(payload, CultureInfo.InvariantCulture)),
                RegistryValueKind.QWord => long.Parse(payload, CultureInfo.InvariantCulture),
                _ => payload,
            };
            using var key = root.CreateSubKey(subKey, writable: true);
            key.SetValue(name, value, kind);
        }
        if (action.GetBoolean("broadcast") == true)
            User32.BroadcastSettingChange(null);
    }

    private static (RegistryKey Root, string SubKey) Open(ComponentConfig action)
    {
        if (!BuiltInActions.TrySplitRegistryKey(action.GetString("key"), out var hive, out var subKey))
            throw new ActionFailedException("'key' must start with HKCU\\ or HKLM\\");
        return (BuiltInActions.RegistryHives[hive] ? Registry.CurrentUser : Registry.LocalMachine, subKey);
    }

    private static RegistryValueKind Kind(string? kind) => kind switch
    {
        "expand_string" => RegistryValueKind.ExpandString,
        "dword" => RegistryValueKind.DWord,
        "qword" => RegistryValueKind.QWord,
        "multi_string" => RegistryValueKind.MultiString,
        _ => RegistryValueKind.String,
    };

    private static object Convert(string value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)(uint)ParseNumber(value)),
        RegistryValueKind.QWord => ParseNumber(value),
        RegistryValueKind.MultiString => value.Split('|'),
        _ => value,
    };

    private static long ParseNumber(string value)
    {
        value = value.Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(value, CultureInfo.InvariantCulture);
    }
}

/// <summary><c>service.control</c>: starts or stops a Windows service (requires admin).</summary>
public sealed class ServiceControlAction : IRevertibleActionHandler
{
    /// <inheritdoc />
    public string Type => "service.control";

    /// <inheritdoc />
    public Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken) =>
        SetAsync(action.GetString("name") ?? "", action.GetString("state") == "running", action.GetDuration("timeout") ?? TimeSpan.FromSeconds(30));

    /// <inheritdoc />
    public Task<string?> CaptureAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        using var service = Open(action.GetString("name") ?? "");
        return Task.FromResult<string?>(service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending ? "running" : "stopped");
    }

    /// <inheritdoc />
    public Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext context, CancellationToken cancellationToken) =>
        SetAsync(action.GetString("name") ?? "", snapshot == "running", action.GetDuration("timeout") ?? TimeSpan.FromSeconds(30));

    private static Task SetAsync(string name, bool running, TimeSpan timeout) => Task.Run(() =>
    {
        using var service = Open(name);
        try
        {
            if (running && service.Status != ServiceControllerStatus.Running)
            {
                if (service.Status == ServiceControllerStatus.Paused)
                    service.Continue();
                else if (service.Status != ServiceControllerStatus.StartPending)
                    service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, timeout);
            }
            else if (!running && service.Status != ServiceControllerStatus.Stopped)
            {
                if (service.Status != ServiceControllerStatus.StopPending)
                    service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            }
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            throw new ActionFailedException($"service '{name}' did not become {(running ? "running" : "stopped")} within {ValueConverter.FormatDuration(timeout)}");
        }
        catch (InvalidOperationException ex)
        {
            throw new ActionFailedException($"cannot {(running ? "start" : "stop")} service '{name}': {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    });

    private static ServiceController Open(string name)
    {
        var service = new ServiceController(name);
        try
        {
            _ = service.Status;
            return service;
        }
        catch (InvalidOperationException)
        {
            service.Dispose();
            throw new ActionFailedException($"there is no service named '{name}'");
        }
    }
}
