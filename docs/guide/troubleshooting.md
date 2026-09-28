# Troubleshooting and FAQ

## First steps

1. Open the **Activity** tab. It says what happened, what did not happen, and why.
2. Open **Settings** in the app (the status is at the top). Is the service connected? Did the file load without errors?
3. Try the automation with **Run now** (Automations tab).
4. Look at the logs:
   - agent: `%LocalAppData%\AutoSettings\logs\agent-YYYYMMDD.log`
   - service: `%ProgramData%\AutoSettings\logs\service-YYYYMMDD.log`

## Common problems

### "My automation does not run"

- Is it enabled (✓ in the Automations tab)? Is AutoSettings paused (header shows *Paused*)?
- Does the Activity tab show the trigger? For app triggers, check the exact exe name in Task Manager → Details.
- `app_started` fires for the **first** instance by default. If the app was already running, it does not fire again
  until all copies have exited. Use `instance: any` to react to every process.
- `app_closed` fires when the **last** instance exits. Apps that keep running in the tray have not closed.
- Is there a cooldown, or is the automation still running a previous `delay`? The Activity tab says so.
- ⏸ in the list means the loop guard suspended it; save the file to resume.

### "The file has errors"

The message includes the line number. Common causes:

- Tabs instead of spaces, or list items that are not aligned.
- A value starting with `{{`, `*` or `[` that is not quoted: write `message: "{{ user }} ..."`.
- Times not quoted: `after: "22:00"`.
- Misspelled types or fields: check the *"Did you mean ...?"* suggestion.

### "Sign-in triggers do not fire"

Sign-in is detected by the service. Check that the service runs: `Get-Service AutoSettings` in PowerShell, or
services.msc. The agent must connect within 5 minutes of the sign-in to receive the event (it normally starts
within seconds).

### "The boot trigger does not fire"

- `boot` only works in **machine** automations (`%ProgramData%\AutoSettings\automations.yaml`).
- With Fast Startup (default on most PCs) Windows does not really shut down. AutoSettings detects Fast Startup boots
  from the Windows event log; if you set `include_fast_startup: false`, only full restarts count.
- Restarting the service does not count as a boot.

### "Brightness does not change"

- Laptop screens work through Windows. External monitors need **DDC/CI** enabled in the monitor's own menu, and
  some monitors (or docks/adapters) do not support it.
- `monitor: internal` / `external` limits which screens are changed.

### "Changing Wi‑Fi or Bluetooth is denied"

Settings → Privacy & security → **Radios**: allow apps to control device radios.

### "The Wi‑Fi condition is always false"

On Windows 11 24H2 and later, desktop apps need **location access** to read the Wi‑Fi network name
(Settings → Privacy & security → Location → *Let desktop apps access your location*).

### "The high performance power plan is not available"

Many modern laptops only have *Balanced*. The error lists the available plans. Use one of those names, or a plan GUID.

### "HDR, scaling, Night light or Do Not Disturb did not change"

- `display.hdr` only works on displays that support HDR (Settings > Display shows *Use HDR*).
- `display.scaling` only allows the steps your display offers (see the Scale list in Settings > Display).
- Night light and Do Not Disturb have no official interface. If a Windows update changes how they are stored, the
  action fails with a message saying so. Use `settings.open` (`page: night_light` or `page: focus`) as a fallback, and
  please open an issue.
- Night light must have been turned on once in Settings before AutoSettings can switch it.

### "The editor says the file has errors"

The visual editor works on the last valid version of the file, so it refuses to open while `automations.yaml` has
errors (for example after a hand edit). Open **Edit whole file**, fix the lines marked in red, and save.

### "A command works in PowerShell but not in AutoSettings"

- Commands run hidden and non-interactive by default. Use `hidden: false` to see the window.
- With `wait: true`, a non-zero exit code counts as a failure and the error output appears in the Activity log.
- `run_as: system` (machine automations only) runs as SYSTEM, which has no user profile, mapped drives or HKCU of
  the user.

### "A setting was not restored after a profile"

The Activity tab lists every restore. If the value could not be read before the profile applied it (device
unplugged, access denied), a warning says so and that setting is not restored.

## FAQ

**Does AutoSettings need to run as administrator?**
Only the installation. The service runs as SYSTEM; your agent runs with your normal rights.

**Does it slow down my PC?**
No. App detection uses Windows event tracing, which is event-driven, not polling. Focus detection uses a
system hook. Automations only run when their triggers fire.

**Can I share automations?**
Yes, the YAML file is plain text. Copy automations between files or share them with others.

**Are comments in my file kept?**
With **Edit whole file** or another text editor, yes. The automation editor (visual/YAML per automation) rewrites the
whole file in a standard format and does not keep comments, like Home Assistant's editor. It asks once before doing so.

**Can I use AutoSettings in Turkish?**
Yes: Settings → Language → Türkçe, then restart the agent (tray icon → Exit, open AutoSettings from the Start menu).
The reference documentation is in English.

**How do I stop everything quickly?**
Tray icon → Pause automations → Until I resume.

**Where is the source of truth for the fields of each action?**
The [reference](../reference/index.md). It is generated from the code, so it is always accurate for your version.
