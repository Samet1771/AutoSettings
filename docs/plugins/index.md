# Plugins

!!! note "New in 0.3"
    Plugin support is new in AutoSettings 0.3 and is being tested in the 0.3 betas.

Plugins add new **triggers**, **conditions** and **actions** to AutoSettings. Once a plugin is installed, its
components appear in the editor next to the built-in ones and work in automations and profiles like them.

```yaml
automations:
  - name: Back up when my USB drive is connected
    triggers:
      - type: acme.usb.connected      # from the plugin "acme.usb"
        drive: "E:"
    actions:
      - type: command.run
        command: robocopy "$env:USERPROFILE\Documents" "$env:AUTOSETTINGS_EVENT_DATA_DRIVE\Backup" /MIR
```

## Two kinds of plugins

| | Script plugins | .NET plugins |
|---|---|---|
| Written in | PowerShell, plus a `plugin.yaml` that describes them | C# (or any .NET language), with the `AutoSettings.Sdk` NuGet package |
| Build step | none | `dotnet build`, then pack |
| Good for | small actions and checks, polling for changes | anything: Windows APIs, events without polling, long-running watchers |
| Triggers | the script runs every few seconds and reports events | your code raises events as they happen |

Both kinds are described by the same [`plugin.yaml` manifest](manifest.md). AutoSettings reads the manifest, not the
plugin's code, to learn what the plugin offers. That is how the editor, validation and documentation work for
plugins without running them.

## Names

A plugin has an id `publisher.name`, for example `acme.usb`. Its components are named `publisher.name.component`, for
example `acme.usb.connected`. Built-in types never have more than one dot (`audio.volume`), so plugin types cannot
clash with them or with each other.

## Personal and machine plugins

- A **personal plugin** is installed by a user, for themselves. It runs as that user in their AutoSettings app and
  can only be used in personal automations.
- A **machine plugin** is installed by an administrator for everyone on the computer. Its actions can run as SYSTEM
  in the AutoSettings service (`runs_as: machine`), like the built-in `registry.set`. Machine automations can use
  them, and personal automations can use its user components.

Before installing, AutoSettings shows the [permissions](manifest.md#permissions) the plugin declares. These are the
author's promise, not a sandbox: a plugin can do anything its user (or SYSTEM) can. **Only install plugins you
trust.**

## When a plugin is missing

If an automation uses a plugin that is not installed or is turned off, AutoSettings keeps the automation. It
marks it **⚠ needs plugin x** and does not run it until the plugin is back. The rest of the file keeps working.

## Where to go next

- [Writing a script plugin](script-plugins.md): PowerShell, nothing to compile.
- [Writing a .NET plugin](dotnet-plugins.md): C# and the SDK.
- [plugin.yaml reference](manifest.md), [Packaging and publishing](publishing.md), [Versions and compatibility](versioning.md).
- [SDK API reference](api/index.md): every type of `AutoSettings.Sdk`.
- Samples: [hello-script](https://github.com/Samet1771/AutoSettings/tree/main/samples/plugins/hello-script) and
  [HelloDotnet](https://github.com/Samet1771/AutoSettings/tree/main/samples/plugins/HelloDotnet).
- For users: [installing and managing plugins](../guide/plugins.md).

## Status

| Part | Status |
|---|---|
| Catalog that can change while the app runs | done (0.3.0-beta.1) |
| Plugin triggers, `{{ event.data.* }}`, missing-plugin handling | done (0.3.0-beta.1) |
| `plugin.yaml` format and the `AutoSettings.Sdk` API | done (this page, [manifest](manifest.md)) |
| Running script plugins (copied into the plugins folder by hand) | done ([guide](script-plugins.md)) |
| Running .NET plugins (separate process per plugin), `autosettings-plugin` tool | done ([guide](dotnet-plugins.md)) |
| Installing from a file or GitHub, updates and rollback (command line) | done ([publishing](publishing.md)) |
| Plugins page in the app ([user guide](../guide/plugins.md)) | done |
| `dotnet new` template, [API reference](api/index.md), [versioning](versioning.md), NuGet packaging | done (publishing needs a NuGet key) |
