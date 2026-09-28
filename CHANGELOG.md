# Changelog

## 0.2.0 (unreleased)

- New app window (Fluent design) with pages for automations, profiles, templates, activity and settings.
- Visual editor and YAML editor with autocomplete, hover help and error squiggles; switch between them at any
  time. Whole-file editor that keeps comments.
- App picker and user picker; file and folder browser.
- Templates: gaming, presentation, night, meeting, battery saver, focus.
- Enable/disable, duplicate, reorder, delete, import/export automations.
- English and Turkish; light, dark or system theme.
- 11 new actions: HDR, main display, display scaling, Night light, accent color, power mode, notification
  banners, Do Not Disturb, keyboard layout, airplane mode, Settings pages.
- Security: placeholders are no longer expanded inside `command.run` scripts; their values are passed as
  `AUTOSETTINGS_*` environment variables instead.
- MSI installer, release workflow, winget manifests, documentation site.
- License changed from MIT to a source-available license: use only; no modification, redistribution or sale.

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
