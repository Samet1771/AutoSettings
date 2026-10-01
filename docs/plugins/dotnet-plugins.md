# Writing a .NET plugin

A .NET plugin is a class library built against the `AutoSettings.Sdk` package. Use one when a script plugin is not
enough: you need Windows APIs, events that happen instantly instead of polling, or long-running watchers. The
complete [HelloDotnet sample](https://github.com/Samet1771/AutoSettings/tree/main/samples/plugins/HelloDotnet) has a
revertible action, a condition and an instant trigger.

## How it runs

Every .NET plugin runs in **its own process**, `AutoSettings.PluginHost.exe`, started the first time AutoSettings
needs the plugin:

- for personal automations, by the user's app, as that user;
- for machine actions (`runs_as: machine`) of machine plugins, by the service, as SYSTEM.

The host loads your assembly and its dependencies from the plugin folder. They are separate from AutoSettings' own
assemblies, so you can use any library and any version. Only `AutoSettings.Sdk` is shared. If the plugin crashes or
hangs, only the host stops: AutoSettings writes it in the Activity page and starts the host again when it is needed.
After 3 crashes in 10 minutes the plugin is turned off until AutoSettings restarts or the plugin is updated. A call
that takes longer than its `timeout` (default 60 seconds) stops the host.

Anything the plugin writes to the console goes to the AutoSettings log files, not to the Activity page. Use
`request.Log` and `sink.Log` for messages users should see.

## 1. The project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>   <!-- or net10.0-windows10.0.19041.0 for Windows APIs -->
    <AssemblyName>Acme.Usb</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <!-- AutoSettings provides the SDK at run time: do not copy it next to the plugin. -->
    <PackageReference Include="AutoSettings.Sdk" Version="1.*" ExcludeAssets="runtime" />
    <None Update="plugin.yaml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`dotnet new autosettings-plugin` creates this for you; see [Tools](#tools).

## 2. plugin.yaml

The manifest has the plugin's details. Leave out `components`: the packing tool writes them from your attributes.

```yaml
id: acme.usb
name: USB tools
version: 1.0.0
publisher: Acme
kind: dotnet
sdk: "1.0"
min_app_version: 0.3.0
entry: Acme.Usb.dll
permissions: [process_launch]
```

Every key is described in the [plugin.yaml reference](manifest.md).

## 3. The entry point

One public class implements `IPlugin`. AutoSettings creates it once (it needs a public parameterless constructor)
and calls `Configure`:

```csharp
using AutoSettings.Sdk;

public sealed class UsbPlugin : IPlugin
{
    public void Configure(IPluginBuilder builder) => builder
        .AddAction(new EjectAction())
        .AddCondition(new DrivePresentCondition())
        .AddTrigger(new DriveWatcher());
}
```

## 4. Actions

```csharp
[PluginComponent("acme.usb.eject", Title = "Eject a USB drive", Description = "Safely removes a USB drive.")]
[Localized("tr", "USB sürücüyü çıkar")]
[Field("drive", FieldKind.String, Description = "Drive letter, for example E:.", Required = true, Example = "E:")]
public sealed class EjectAction : IPluginAction
{
    public async Task ExecuteAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        var drive = request.Parameters.GetString("drive")!;
        if (!Directory.Exists(drive + "\\"))
            throw new PluginActionException($"drive {drive} is not connected");   // shown in the Activity page
        await Ejector.EjectAsync(drive, cancellationToken);
        request.Log.Info($"Ejected {drive}.");
    }
}
```

- `[PluginComponent]` names the type used in automations. It must start with the plugin id: `acme.usb.` + a name
  in lower-case letters, digits and underscores.
- Each `[Field]` declares a parameter. The [field types](manifest.md#fields) decide how values are checked and which
  control the editor shows. Values arrive checked and with defaults filled in, and placeholders such as
  `{{ user }}` are already replaced.
- `request.Parameters` has typed getters: `GetString`, `GetInteger`, `GetNumber`, `GetBoolean`, `GetStringList`,
  `GetDuration` and `GetTime`.
- `request.Event` is what started the automation (`null` for manual runs), and `request.User` is the user it runs for.
- Throw `PluginActionException` with a plain-language message when the action fails. Other exceptions are reported
  as unexpected errors.
- Honour the `CancellationToken`: it is cancelled when AutoSettings stops.

### Revertible actions

Implement `IRevertiblePluginAction` so profiles can undo the action. Before a profile changes a setting for the
first time, AutoSettings calls `CaptureAsync` and keeps the returned text. When the profile ends, it calls
`RestoreAsync` with that text. Mark the fields that say *which* setting changes with `KeyField = true`:

```csharp
[PluginComponent("acme.usb.label", Title = "Rename a drive", Description = "Sets a drive's label.")]
[Field("drive", FieldKind.String, Description = "Drive letter.", Required = true, KeyField = true)]
[Field("label", FieldKind.String, Description = "New label.", Required = true)]
public sealed class LabelAction : IRevertiblePluginAction
{
    public Task ExecuteAsync(ActionRequest request, CancellationToken ct) =>
        Labels.SetAsync(request.Parameters.GetString("drive")!, request.Parameters.GetString("label")!);

    public async Task<string?> CaptureAsync(ActionRequest request, CancellationToken ct) =>
        await Labels.GetAsync(request.Parameters.GetString("drive")!);

    public Task RestoreAsync(ActionRequest request, string? snapshot, CancellationToken ct) =>
        Labels.SetAsync(request.Parameters.GetString("drive")!, snapshot ?? "");
}
```

## 5. Conditions

```csharp
[PluginComponent("acme.usb.present", Title = "USB drive present", Description = "True while the drive is connected.")]
[Field("drive", FieldKind.String, Description = "Drive letter.", Required = true)]
public sealed class DrivePresentCondition : IPluginCondition
{
    public ValueTask<bool> EvaluateAsync(ConditionRequest request, CancellationToken ct) =>
        ValueTask.FromResult(Directory.Exists(request.Parameters.GetString("drive") + "\\"));
}
```

Conditions run every time an automation is triggered, so keep them fast. An exception counts as false and is noted in
the Activity page.

## 6. Triggers

A trigger class watches for something and raises events through the sink. Put one `[PluginComponent]` on it for every
event it raises. Its `[Field]`s filter the events, comparing each field with the event value of the same name:

```csharp
[PluginComponent("acme.usb.connected", Title = "USB drive connected", Description = "...", OppositeEvent = "acme.usb.disconnected")]
[PluginComponent("acme.usb.disconnected", Title = "USB drive removed", Description = "...")]
[Field("drive", FieldKind.String, Description = "Only this drive letter; leave empty for any.")]
public sealed class DriveWatcher : IPluginTrigger
{
    public async Task RunAsync(ITriggerSink sink, CancellationToken cancellationToken)
    {
        using var watcher = new DeviceWatcher();
        watcher.Arrived += drive => sink.Raise("acme.usb.connected", new Dictionary<string, string> { ["drive"] = drive });
        watcher.Removed += drive => sink.Raise("acme.usb.disconnected", new Dictionary<string, string> { ["drive"] = drive });
        await Task.Delay(Timeout.Infinite, cancellationToken);   // run until AutoSettings stops the trigger
    }
}
```

- `RunAsync` must keep running until the token is cancelled.
- AutoSettings only starts triggers while an automation uses one of them, and stops them when none does.
- Event values are text. Actions can use them as `{{ event.data.drive }}`.
- `OppositeEvent` lets profiles applied by this event (`revert_on: auto`) be reverted by the opposite one.
- Pass `user:` to `Raise` when the event is about a particular user; the `user` filter of triggers uses it.

## 7. Translations

Titles and descriptions are English. Add other languages with `[Localized]`. On classes with several components,
set `Type`:

```csharp
[Localized("tr", "USB sürücü takıldı", Type = "acme.usb.connected")]
```

## 8. Machine plugins

Actions with `RunsAs = PluginScope.Machine` run in the service as SYSTEM. They need `scope: machine` and the
`run_as_system` permission in `plugin.yaml`, and only machine automations can use them. An administrator installs
the plugin for the whole computer. Everything else of a machine plugin (user actions, conditions, triggers in
personal automations) runs as the user. Be careful with SYSTEM code: treat parameters as untrusted input.

## Tools

Start a new plugin from the template (`--kind script` for a PowerShell plugin):

```powershell
dotnet new install AutoSettings.Templates
dotnet new autosettings-plugin --name UsbTools --publisher acme   # plugin id: acme.usbtools
```

The `autosettings-plugin` tool packs and checks plugins:

```powershell
dotnet tool install --global AutoSettings.PluginTool
dotnet build -c Release
autosettings-plugin describe bin\Release\net10.0\Acme.Usb.dll   # the components your attributes declare
autosettings-plugin validate bin\Release\net10.0                # check without packing
autosettings-plugin pack bin\Release\net10.0 --out dist         # dist\acme.usb-1.0.0.aspkg + SHA256SUMS.txt
```

The SDK, the tool and the templates are NuGet packages (see [Versions](versioning.md#packages-on-nuget)). Until they are
published, use them from a clone of the AutoSettings repository: reference `src/AutoSettings.Sdk` with
`Private="false"`, run the tool with `dotnet run --project src/AutoSettings.PluginTool -- pack ...`, and install the
template with `dotnet new install templates/content/autosettings-plugin`.

Every public type and member of the SDK is described in the [API reference](api/index.md).

While developing, unpack the package (or copy the build output plus the packed `plugin.yaml`) to
`%LocalAppData%\AutoSettings\plugins\<id>\<version>`. AutoSettings loads it within seconds. To debug, attach Visual
Studio to `AutoSettings.PluginHost.exe` after the plugin was used once.
