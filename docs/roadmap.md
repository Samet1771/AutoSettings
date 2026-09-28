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

## Done (v0.2)

- **Milestone 4 – Editor**: visual editor with cards generated from the catalog, app picker (running and installed
  apps, with icons) and user picker; YAML view with highlighting, autocomplete, hover help and error squiggles
  (AvalonEdit), switching back and forth like Home Assistant; whole-file editor that keeps comments; template
  gallery (gaming, presentation, night, meeting, battery saver, focus); enable/disable, duplicate, reorder,
  import/export; Fluent look (WPF-UI) with light/dark theme; English and Turkish.
- **Milestone 5 – More settings**: HDR, main display, display scaling, Night light, accent color, power mode,
  notification banners, Do Not Disturb, keyboard layout, airplane mode, Settings pages (`settings.open`).
- **Milestone 6 – Distribution**: MSI installer (WiX) with service registration, Start-menu entry and upgrades;
  release workflow with optional code signing; winget manifests; documentation site on GitHub Pages; automatic
  updates from GitHub Releases.

## Next

- Submit the winget package once the first release is published.
- Screenshots in the README and the guide.
- Testing on more hardware (HDR monitors, DDC/CI brightness, multi-monitor scaling).

## Ideas

- More triggers: time schedule, network connected/disconnected, power plugged/unplugged, display connected,
  USB device connected, idle/active, hotkey.
- `revert_after: 30m` for time-limited profiles.
- Per-automation run modes (restart, queue).
