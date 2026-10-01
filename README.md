<p align="center">
  <img src="docs/assets/icon.png" width="96" alt="AutoSettings icon">
</p>

<h1 align="center">AutoSettings</h1>

<p align="center">
  <b>Windows settings that change themselves.</b><br>
  Switch power plans, audio devices, dark mode, brightness, Wi‑Fi and more — automatically — when the computer starts,
  when someone signs in, and when apps start, close, gain focus or lose focus.
</p>

---

## What it does

AutoSettings runs quietly in the background (it starts with Windows) and follows simple rules you write:

> **When** *PowerPoint is focused* **→** mute sounds, keep the screen on, use light mode.
> When you switch away, everything goes back to how it was.

```yaml
automations:
  - id: presentation-mode
    name: Presentation mode while PowerPoint is focused
    triggers:
      - type: app_focused
        app: POWERPNT.EXE
    actions:
      - type: profile.apply
        profile: presentation   # undone automatically when PowerPoint loses focus

profiles:
  - id: presentation
    actions:
      - type: audio.mute
        state: mute
      - type: power.screen_timeout
        minutes: 0
      - type: theme.mode
        mode: light
```

### Triggers

| Trigger | Fires when |
|---|---|
| `boot` | the computer starts (including Fast Startup boots) |
| `logon` / `logoff` | a user signs in / out — you know **which** user, and can filter by user |
| `lock` / `unlock` | the screen is locked / unlocked |
| `app_started` / `app_closed` | an app starts / exits (first / last instance, so multi-process apps work) |
| `app_focused` / `app_unfocused` | an app's window gains / loses focus |

### Highlights

- **Profiles with automatic undo** — apply a set of settings while an app is running or focused; AutoSettings remembers the previous values and restores them afterwards. Overlapping profiles resolve by priority.
- **Conditions** — time of day, weekdays, power source, battery level, Wi‑Fi network, number of monitors, other apps running, full-screen, user, and `and`/`or`/`not`.
- **Actions** — light/dark mode, wallpaper, taskbar, brightness (laptop + DDC/CI monitors), resolution & refresh rate, power plans and timeouts, volume, mute, default audio devices, Wi‑Fi/Bluetooth/airplane mode, HDR, main display, scaling, Night light, accent color, power mode, notification banners and Do Not Disturb, keyboard layout, mouse speed, launch/close apps, PowerShell/cmd scripts, notifications, URLs and `ms-settings:` pages, and — for everything else — registry values and services.
- **Personal and machine automations** — each user has their own; administrators can add machine-wide ones (e.g. at boot or for a specific user signing in).
- **Visual and YAML editor, like Home Assistant** — build automations with forms (app and user pickers included) or write YAML with autocomplete, hover help and error squiggles; switch between them at any time.
- **Templates** — gaming, presentation, night, meeting, battery saver and focus, ready to adjust.
- **Plugins** — add new triggers, conditions and actions written in PowerShell or C#: install them from a file or GitHub, update and turn them off from the Plugins page. .NET plugins run in their own process, so a broken plugin cannot take AutoSettings down. A [documented SDK](docs/plugins/index.md) with samples, a `dotnet new` template and a packing tool.
- **Automatic updates** from GitHub releases — asks before installing by default; checksums verified.
- **English and Turkish**, light and dark theme.
- **Readable YAML with helpful errors** — line numbers and "did you mean ...?" suggestions; a bad edit never replaces a working configuration.
- **Activity timeline** — see what fired, why something did *not* run, and what changed.
- **Safety** — loop guard, dry-run mode, test button, pause from the tray.

## Getting started

1. Download `AutoSettings-x.y.z-x64.msi` from the [releases](https://github.com/Samet1771/AutoSettings/releases) and run it.
   (A portable zip with an install script is there too.)
2. The tray icon appears. Open it and turn on an example, or pick one from **Templates**.
3. Double-click an automation to change it in the visual or the YAML editor.

Full guide: **[docs/guide/getting-started.md](docs/guide/getting-started.md)**.

## Documentation

| | |
|---|---|
| [Installation](docs/guide/installation.md) | install, update, uninstall |
| [Getting started](docs/guide/getting-started.md) | your first automation in 5 minutes |
| [Writing automations](docs/guide/automations.md) | the file format, triggers, conditions, actions, placeholders |
| [Profiles](docs/guide/profiles.md) | apply-and-revert sets of settings, priorities |
| [Personal vs. machine automations](docs/guide/personal-and-machine.md) | who runs what, and with which rights |
| [The app](docs/guide/the-app.md) | window, tray menu, activity timeline, settings, pause, dry run |
| [The editor](docs/guide/editor.md) | visual and YAML editor, templates, import/export |
| [Updates](docs/guide/updates.md) | automatic updates, beta versions, company settings |
| [Plugins](docs/guide/plugins.md) | installing, updating and removing plugins |
| [Writing plugins](docs/plugins/index.md) | script and .NET plugins, `plugin.yaml`, packaging, the SDK API reference |
| [Examples](docs/examples/) | gaming mode, presentations, day/night, meetings, company PCs |
| [Reference](docs/reference/index.md) | every trigger, condition and action with all fields (generated) |
| [Troubleshooting & FAQ](docs/guide/troubleshooting.md) | logs, common problems |
| [Architecture](docs/dev/architecture.md) and [developer docs](docs/dev/building.md) | how it works, how to build, how to add an action |
| [Roadmap](docs/roadmap.md) | what is done and what comes next |

## Requirements

Windows 10 1809 or later, or Windows 11 (x64). Administrator rights to install (the service runs as SYSTEM).

## License

Source-available, all rights reserved: you may read the code and download, install and use the official
releases, but not modify, redistribute or sell it. See [LICENSE](LICENSE).
