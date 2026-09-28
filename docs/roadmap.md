# Roadmap

## Done (v0.1)

- **Milestone 1 – Skeleton**: solution, Windows Service, per-session agent started by the service, named-pipe
  JSON-RPC, logging, YAML configuration store with hot reload.
- **Milestone 2 – All six triggers**: boot (including Fast Startup), logon (with user identity), app started, app
  closed, app focused, app unfocused; plus logoff, lock and unlock.
- **Milestone 3 – Rule engine**: conditions (time, user, power, battery, Wi‑Fi, monitors, apps, full-screen,
  profiles, and/or/not), cooldowns, loop guard, dry run, placeholders, profiles with automatic, priority-aware revert,
  and 24 actions across personalization, display, power, audio, devices, apps/scripts and advanced (registry,
  services).
- Tray app with pause, profiles, activity timeline and a validating YAML editor.
- Generated reference docs and JSON Schema, user and developer documentation, CI with a self-contained build.

## Next

### Milestone 4 – Editor

- Visual editor: "When [app] [is focused] and [on battery] → apply [profile]" forms generated from the catalog,
  with an app picker (running and installed apps, with icons) and a user picker.
- YAML view with autocomplete, hover help and inline errors (Monaco in WebView2, driven by the JSON Schema),
  switching back and forth like Home Assistant.
- Template gallery: Gaming, Presentation, Night, Meeting, Battery saver, Focus.
- Enable/disable from the list; import/export.
- Modern look (WPF-UI Fluent), English + Turkish.

### Milestone 5 – More settings

Night light, HDR, display scaling, primary monitor, accent color, Do Not Disturb / notification banners,
keyboard layout, airplane mode, default browser page, and more `ms-settings` helpers. Suggestions welcome.

### Milestone 6 – Distribution

- MSI installer (WiX) with service registration, Start-menu entry and upgrades; winget package.
- Signed binaries, release workflow, documentation site (MkDocs) on GitHub Pages.

## Ideas

- More triggers: time schedule, network connected/disconnected, power plugged/unplugged, display connected,
  USB device connected, idle/active, hotkey.
- `revert_after: 30m` for time-limited profiles.
- Per-automation run modes (restart, queue).
