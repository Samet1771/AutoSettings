# Writing automations

Automations live in a YAML file:

- **Personal automations**: `%AppData%\AutoSettings\automations.yaml`. They run as you, in your session.
- **Machine automations**: `%ProgramData%\AutoSettings\automations.yaml`. Administrators only; they run in the service.
  See [Personal and machine automations](personal-and-machine.md).

You can edit the file in the app's **YAML editor** tab, or in any text editor. It is reloaded automatically when it
changes. If it contains errors, the previous version keeps running and the errors are shown in the app, in the
Activity tab and as a notification.

## File structure

```yaml
version: 1            # file format version, always 1 for now

automations:          # the rules
  - id: ...
    ...

profiles:             # named sets of actions (optional)
  - id: ...
    ...
```

## An automation

```yaml
  - id: gaming-mode                  # unique id (letters, digits, dashes). Generated from the name if missing.
    name: Gaming mode                # shown in the app and the activity log (optional)
    description: For Steam games     # notes for yourself (optional)
    enabled: true                    # false keeps the automation but never runs it (optional, default true)
    cooldown: 30s                    # ignore triggers for this long after a run (optional)
    triggers:                        # WHEN: any one of these starts the automation (at least one)
      - type: app_started
        app: steam.exe
    conditions:                      # IF: all of these must be true (optional)
      - type: power_source
        is: ac
    actions:                         # THEN: run in order (at least one)
      - type: profile.apply
        profile: gaming
```

Every trigger, condition and action is a small map with a `type` and its fields. The
**[reference](../reference/index.md)** lists all types, their fields, defaults and an example for each.

### Shortcuts

- A trigger without fields can be written as just its type: `triggers: [lock, unlock]`.
- `trigger:`, `condition:` and `action:` (singular) are accepted as well as the plural forms.
- A list with one value can be written without brackets: `app: chrome.exe` is the same as `app: [chrome.exe]`.

## Triggers

| Trigger | When | Notes |
|---|---|---|
| [`boot`](../reference/triggers/boot.md) | The computer started | Machine automations only. Also fires after a *Fast Startup* boot (the default "shut down" on most PCs), unless `include_fast_startup: false`. Restarting the service does not fire it again. |
| [`logon`](../reference/triggers/logon.md) | A user signed in | The user is known: filter with `user:` and use `{{ user }}` in actions. `session: local/remote` separates console and Remote Desktop sign-ins. |
| [`logoff`](../reference/triggers/logoff.md) | A user signed out | |
| [`lock`](../reference/triggers/lock.md) / [`unlock`](../reference/triggers/unlock.md) | The screen was locked / unlocked | Unlocking is not a new sign-in. |
| [`app_started`](../reference/triggers/app_started.md) | An app started | By default only when the **first** copy starts (`instance: first`); many apps run several processes. |
| [`app_closed`](../reference/triggers/app_closed.md) | An app exited | By default only when the **last** copy exits (`instance: last`). |
| [`app_focused`](../reference/triggers/app_focused.md) | An app's window became the active window | Personal automations only. Focus must stay for 250 ms (Alt+Tab is ignored). Optional `title:` filter. |
| [`app_unfocused`](../reference/triggers/app_unfocused.md) | The app is no longer the active window | Personal automations only. |

### Matching apps

`app:` accepts one or more patterns:

| Pattern | Matches |
|---|---|
| `chrome` or `chrome.exe` | the executable name, with or without `.exe`, any letter case |
| `*steam*` | wildcards: `*` any text, `?` one character |
| `C:\Games\*\game.exe` | patterns with a `\` are matched against the full path |
| `*` | any app |

Tip: Task Manager → Details shows the executable names of running apps.

### Matching users

`user:` (triggers) and `users:` (the `user` condition) accept a user name (`samet`), `DOMAIN\name` or
`COMPUTER\name`, a SID (`S-1-5-21-...`), or wildcards (`guest*`).

## Conditions

All conditions must be true. For "either/or", use `or`; to negate, use `not`:

```yaml
    conditions:
      - type: or
        conditions:
          - type: power_source
            is: battery
          - type: not
            conditions:
              - type: wifi
                ssid: HomeWiFi
```

Available: `time` (time window and weekdays, windows can cross midnight), `user`, `power_source`, `battery`,
`wifi`, `monitor_count`, `app_running`, `app_focused`, `fullscreen`, `profile_active`, and `and`/`or`/`not`.
See the [reference](../reference/index.md#conditions).

## Actions

Actions run one after another. If one fails, the automation stops and the error is shown in the Activity tab,
unless that action has `continue_on_error: true`.

```yaml
    actions:
      - type: app.close
        app: Discord.exe
        continue_on_error: true     # fine if Discord is not running or refuses
      - type: delay
        duration: 5s
      - type: notify
        message: "Focus mode on"
```

Categories:

- **Flow**: `profile.apply`, `profile.revert`, `delay`
- **Personalization**: `theme.mode`, `theme.transparency`, `wallpaper.set`, `taskbar.autohide`
- **Display**: `display.brightness`, `display.resolution`
- **Power**: `power.plan`, `power.screen_timeout`, `power.sleep_timeout`
- **Audio**: `audio.volume`, `audio.mute`, `audio.default_device`
- **Network & devices**: `radio.set` (Wi‑Fi, Bluetooth, mobile broadband), `mouse.speed`
- **Apps & scripts**: `app.launch`, `app.close`, `command.run`, `notify`, `open`
- **Advanced**: `registry.set`, `service.control`

### Any other Windows setting

Many Windows settings have no official programming interface. For those:

- **`registry.set`** writes the registry value behind the setting. With `broadcast: true`, running apps are told
  that settings changed. In a profile, the old value is restored (or deleted again) when the profile is reverted.
- **`command.run`** runs PowerShell, so anything you can script, you can automate.
- **`open`** with an `ms-settings:` link opens the right Settings page when a setting cannot be changed automatically.

More dedicated actions are added over time; see the [roadmap](../roadmap.md).

## Placeholders

Text fields of actions can include details of the event, such as `{{ user }}`, `{{ app }}`, `{{ window_title }}`
or `{{ now }}`. See [placeholders](../reference/placeholders.md). A value that **starts** with `{{` must be quoted.

Scripts (`command.run`) are the exception: for safety, placeholders are not pasted into script text. The same values
are available to the script as environment variables, e.g. `$env:AUTOSETTINGS_USER` or `$env:AUTOSETTINGS_APP_PATH`.

## Cooldown and "still running"

- While an automation's actions are running (for example during a `delay`), new triggers for it are ignored.
- `cooldown: 5m` additionally ignores triggers for 5 minutes after it started.
- The **loop guard** suspends an automation that runs more than 20 times in a minute (for example two automations
  that keep undoing each other). Saving the file again, or restarting the app, resumes it.

## Checking your work

- The YAML editor's **Check** button (and every save) validates the whole file: unknown types, missing or invalid
  fields, unknown profiles, and things that are not allowed in this file are reported with their line number,
  often with a *"Did you mean ...?"* suggestion.
- Unknown fields are **warnings** (ignored), everything else is an **error** (the file is not used).
- **Run now** / **Run, ignoring conditions** in the Automations tab test an automation immediately.
- **Dry run** logs what would happen without changing anything.

## Tips

- Quote times: `after: "22:00"`.
- Quote text that starts with `{{`, `*`, `[`, `{`, `&`, `!`, `%`, `@` or looks like a number or `yes`/`no`.
- Use `|` for multi-line scripts:
  ```yaml
      - type: command.run
        command: |
          Get-Process OneDrive -ErrorAction SilentlyContinue | Stop-Process
          Write-Output "done"
  ```
- Comments start with `#`. They are kept when you edit the file yourself.
- VS Code users: install the *YAML* extension and add this line at the top of the file for autocomplete:
  `# yaml-language-server: $schema=https://raw.githubusercontent.com/Samet1771/WindowsSettingAutomation/main/docs/reference/automations.schema.json`
