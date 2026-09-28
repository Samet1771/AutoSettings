# Changelog

## 0.1.0 (unreleased)

First version.

- Windows Service + per-user tray agent; starts with Windows.
- Triggers: boot (incl. Fast Startup), logon, logoff, lock, unlock, app started/closed (first/last instance),
  app focused/unfocused (with window-title filter).
- Conditions: time and weekdays, user, power source, battery, Wi‑Fi, monitor count, app running, app focused,
  full-screen, profile active, and/or/not.
- 24 actions: light/dark mode, transparency, wallpaper, taskbar auto-hide, brightness (WMI + DDC/CI), resolution and
  refresh rate, power plan, screen/sleep timeouts, volume, mute, default audio device, Wi‑Fi/Bluetooth radios, mouse
  speed, launch/close app, run command, notification, open URL/settings page, registry value, service, delay,
  apply/revert profile.
- Profiles with automatic, priority-aware revert.
- Personal and machine automation files with validation, line-numbered errors and suggestions.
- Activity timeline, pause, dry run, test run, validating YAML editor.
- Generated reference documentation and JSON Schema.
