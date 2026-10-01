# Changelog

## 0.3.0 (unreleased)

- Work in progress: plugin support (actions, conditions and triggers from .NET or PowerShell plugins) with a documented SDK.
  - The component catalog can now change while the app runs, so installed plugins can add their components
    (no visible change yet).
  - Plugins can add their own triggers. A trigger can filter on the event's values, and its values are available
    as `{{ event.data.<name> }}`. A profile applied by a plugin event can be reverted by the opposite event.
  - An automation that uses a plugin that is not installed or is turned off is kept and marked "needs plugin" in
    the list. It does not run until the plugin is back; the rest of the file keeps working.
  - `AutoSettings.Sdk`, the API for .NET plugins, and the `plugin.yaml` manifest format, with its reference in
    the docs (Plugins section).
  - Script plugins run: PowerShell actions (revertible ones included), conditions and polling triggers. Copy a plugin
    into `%LocalAppData%\AutoSettings\plugins` (or `%ProgramData%\AutoSettings\plugins` for machine plugins) and
    it loads within seconds. The service only runs machine plugins from folders that only administrators can change.
    Sample: `samples/plugins/hello-script`. Guide: "Writing a script plugin".
  - .NET plugins run, each in its own `AutoSettings.PluginHost.exe` process. A crash or hang never takes AutoSettings
    down; the host restarts when needed, and a plugin that crashes 3 times in 10 minutes is turned off. Triggers
    react instantly. The `autosettings-plugin` tool packs (`.aspkg`), validates and describes plugins.
    Sample: `samples/plugins/HelloDotnet`. Guide: "Writing a .NET plugin".
  - Plugins can be installed from a `.aspkg` file or a GitHub repository, updated (with checksum checks) and rolled
    back, from the command line: `AutoSettings.Agent.exe --plugin list|install|uninstall|enable|disable|update|rollback`.
    Machine plugins ask for administrator permission. A plugin uninstalled while running is removed once it stopped.
  - New **Plugins** page: install from a file or from GitHub (with a dialog showing the plugin's declared permissions
    and who it is installed for), turn on/off, update, go back to the previous version, remove. .NET plugins show
    whether they are signed.

## 0.2.0 (2026-09-30)

- Fixed: `taskbar.autohide` reported success on Windows 11 although the taskbar did not change. It now checks
  the result, saves the setting where Explorer reads it, and can restart Explorer (`restart_explorer: true`).
- Fixed: the app did not connect to the AutoSettings service ("Refusing pipe server ...: it is not a Windows
  service"), so sign-in automations and machine features did not work.
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
- The project moved to https://github.com/Samet1771/AutoSettings; updates come from there.
- Automatic updates from GitHub Releases: ask first (default), install automatically, only notify or off; optional
  beta versions; checksum and signature verification; administrators can lock the settings.
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
