# plugin.yaml reference

Every plugin has a `plugin.yaml` at the root of its folder. It says who made the plugin and what it offers.
AutoSettings reads it to validate automations, fill the editor and show the plugin before it is installed, all
without running the plugin's code.

## Example

```yaml
id: acme.usb
name:
  en: USB tools
  tr: USB araçları
description: Reacts to USB drives being connected and removed.
version: 1.2.0
publisher: Acme
homepage: https://github.com/acme/autosettings-usb
kind: script
scope: user
sdk: "1.0"
min_app_version: 0.3.0
permissions: [powershell]
update:
  github: acme/autosettings-usb

components:
  - type: acme.usb.connected
    kind: trigger
    title:
      en: USB drive connected
      tr: USB sürücü takıldı
    description: Fires when a USB drive is connected.
    opposite: acme.usb.disconnected
    interval: 10s
    scripts:
      poll: poll.ps1
    fields:
      - name: drive
        type: string
        description: Only this drive letter, for example E:. Leave empty for any drive.

  - type: acme.usb.disconnected
    kind: trigger
    title: USB drive removed
    description: Fires when a USB drive is removed.

  - type: acme.usb.label
    kind: action
    title: Rename a drive
    description: Sets the volume label of a drive.
    revertible: true
    scripts:
      apply: label/set.ps1
      capture: label/get.ps1
      restore: label/set.ps1
    fields:
      - name: drive
        type: string
        description: Drive letter.
        required: true
        key: true
        example: "E:"
      - name: label
        type: string
        description: The new label.
        required: true
```

## Top level

| Key | Required | Meaning |
|---|---|---|
| `id` | yes | The plugin id: `publisher.name` in lower-case letters, digits and dashes, for example `acme.usb-tools`. It never changes; updates keep the same id. |
| `name` | yes | Display name. [Translatable](#translations). |
| `description` | | What the plugin does. Translatable. |
| `version` | yes | The plugin version, [semantic versioning](https://semver.org): `1.2.0`, `2.0.0-beta.1`. |
| `publisher` | yes | Who made it. |
| `homepage` | | A web page about the plugin. |
| `kind` | yes | `script` (PowerShell) or `dotnet`. |
| `scope` | | `user` (default): installed by and for one user. `machine`: installed by an administrator for everyone; needed for `runs_as: machine`. |
| `sdk` | yes | The SDK version the plugin was written for, as text: `"1.0"`. AutoSettings loads plugins whose SDK **major** version it supports (currently 1). |
| `min_app_version` | | The oldest AutoSettings version the plugin works with, for example `0.3.0`. Betas of that version count. |
| `entry` | .NET only | The plugin's `.dll`, relative to the plugin folder. |
| `permissions` | | What the plugin does with the computer; see [Permissions](#permissions). |
| `update` | | Where updates are published; see [Updates](#updates). |
| `components` | yes | The triggers, conditions and actions; see [Components](#components). |

## Components

Each item of `components` is a trigger, a condition or an action.

| Key | For | Meaning |
|---|---|---|
| `type` | all | The type used in automations: the plugin id, a dot and a name of lower-case letters, digits and underscores. For example `acme.usb.connected`. |
| `kind` | all | `trigger`, `condition` or `action`. |
| `title` | all | Short title, for example "USB drive connected". Translatable. |
| `description` | all | What it does, in plain language. Translatable. |
| `category` | all | The group in the editor's Add menu. Defaults to the plugin name. |
| `fields` | all | Its parameters; see [Fields](#fields). |
| `example` | all | A YAML example for the documentation. Defaults to the type and the required fields. |
| `notes` | all | Extra notes for the documentation (requirements, limitations). |
| `runs_as` | actions | `user` (default) runs in the user's app as that user. `machine` runs in the service as SYSTEM, which needs `scope: machine` and the `run_as_system` permission. Such actions can only be used in machine automations. |
| `revertible` | actions | `true` when profiles can undo the action. The plugin then saves the current value before the change and restores it later. |
| `available_in` | triggers, conditions | `both` (default), `user` or `machine`: which automation files may use it. |
| `event` | triggers | The name of the plugin event the trigger reacts to. Defaults to the type. |
| `opposite` | triggers | The event that undoes this one. A profile applied by this trigger (`revert_on: auto`) is reverted when the opposite event happens, like `app_focused` and `app_unfocused`. |
| `scripts` | script plugins | The PowerShell files, relative to the plugin folder; see [Scripts](#scripts). |
| `interval` | script triggers | How often the `poll` script runs: `10s`, `1m`… At least `5s`; default `30s`. |
| `timeout` | all | How long one call may take before it is stopped, between `1s` and `30m`; default `60s`. |

### Triggers and events

A plugin raises **events**. Each has a name, for example `acme.usb.connected`, and optional text values, for
example `drive = E:`. A trigger runs the automation when its event happens and every field set in the trigger
matches:

- text fields are compared with the value of the same name, with `*` and `?` wildcards and ignoring case;
- list fields match when any item matches;
- other fields must be equal.

Every trigger also gets the standard `user` field, which filters by the user the event is about. Actions can use the
values as `{{ event.data.drive }}`. In `command.run` scripts they are the environment variable
`$env:AUTOSETTINGS_EVENT_DATA_DRIVE`.

!!! tip
    A trigger field with a `default` always filters events. Leave filter fields without a default, so an empty
    field means "any".

## Fields

| Key | Meaning |
|---|---|
| `name` | Name used in automations: lower-case letters, digits and underscores, starting with a letter. `type`, `user` and `continue_on_error` are reserved. |
| `type` | One of the types below. Default `string`. |
| `description` | What the field means. Required. Translatable. |
| `required` | `true` when the automation must set it. |
| `default` | Value used when the automation does not set it, written as in an automation (`30s`, `true`, `[a, b]`). |
| `values` | For `enum` (required) and `string_list`: the allowed values. |
| `min`, `max` | For `integer` and `number`: limits. |
| `example` | Example value, for the editor and the documentation. |
| `key` | For revertible actions: `true` when the field says *which* setting is changed (for example the drive letter). Two actions with different key values change different settings. |
| `placeholders` | For text: `false` to stop `{{ placeholders }}` being replaced. Use it for fields that are code or commands. Default `true`. |

| Type | Value | Editor |
|---|---|---|
| `string` | one line of text | text box |
| `multiline` | several lines | large text box |
| `path` | a file or folder | text box with Browse |
| `integer` | a whole number | text box |
| `number` | a number with decimals | text box |
| `boolean` | `true` or `false` | yes/no list |
| `enum` | one of `values` | list |
| `duration` | `30s`, `5m`, `1h30m`, `500ms` | text box |
| `time` | a time of day, `22:00` | text box |
| `string_list` | a list of text | text box, comma-separated |
| `app_list` | apps: exe names or paths, wildcards allowed | app picker |
| `user_list` | user names, `DOMAIN\name`, SIDs or wildcards | user picker |

## Translations

`name`, `description`, `title` and field `description` are plain text (English), or a map of languages with `en`
required:

```yaml
title:
  en: USB drive connected
  tr: USB sürücü takıldı
```

The app shows the text in the language chosen under Settings and falls back to English.

## Permissions

`permissions` lists what the plugin does with the computer. AutoSettings shows them before the plugin is installed.
They are **declared, not enforced**: a plugin can do anything its user (or SYSTEM, for machine actions) can.

| Name | Meaning |
|---|---|
| `run_as_system` | Runs parts of itself as SYSTEM, with full control of the computer. Required for `runs_as: machine`. |
| `network` | Uses the network or the internet. |
| `registry_machine` | Changes machine-wide registry settings (HKEY_LOCAL_MACHINE). |
| `filesystem_machine` | Changes files outside the user's own folders. |
| `process_launch` | Starts other programs. |
| `powershell` | Runs PowerShell scripts. Script plugins always have it; it is added if missing. |

## Updates

```yaml
update:
  github: acme/autosettings-usb   # owner/repository
  asset: "*.aspkg"                # which release file to download (default)
```

AutoSettings looks for newer versions in the repository's GitHub releases, the same way it updates itself. It
checks the file against the release's `SHA256SUMS.txt` or the checksum GitHub reports.

## Scripts

A script plugin names one PowerShell file per job, relative to the plugin folder. The files must stay inside the
folder: no `..`, no drive letters and no absolute paths.

| Component | Scripts |
|---|---|
| action | `apply` (required). Revertible actions also need `capture` and `restore`. |
| condition | `evaluate` |
| trigger | `poll` (optional: one trigger's poll script can raise the events of the others, such as `connected` and `disconnected`) |

How the scripts receive parameters and report results is described in
[Writing a script plugin](script-plugins.md).

## .NET plugins

For `kind: dotnet`, `entry` names the plugin's assembly. It contains a class that implements
`AutoSettings.Sdk.IPlugin`. Its components are classes with `[PluginComponent]` and `[Field]` attributes, and the
packing tool writes the `components` section from them, so you do not write it by hand. See
[Writing a .NET plugin](dotnet-plugins.md).

## Checks

A plugin is refused, with a message that says why, when:

- the id, version or a type does not follow the rules above, or a type does not start with the plugin id;
- `sdk` names a major version this AutoSettings does not support, or `min_app_version` is newer than the app;
- a script or `entry` path leaves the plugin folder;
- an action has `runs_as: machine` in a `scope: user` plugin, or without the `run_as_system` permission;
- a field is invalid: reserved name, `enum` without `values`, a default that does not fit the type, and so on.
