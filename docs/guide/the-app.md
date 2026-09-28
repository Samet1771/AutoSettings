# The app

AutoSettings has two parts:

- the **service** (no window): starts with Windows, notices boot, sign-ins, locks and app starts/exits, and runs
  machine automations;
- the **agent** (the tray icon): runs in your session, notices which app has focus, runs your personal automations and
  shows the window.

## Tray icon

Double-click it to open the window. Right-click for:

| Menu item | What it does |
|---|---|
| **Open AutoSettings** | Opens the window. |
| **Pause automations** → 15 minutes / 1 hour / until I resume | No automation starts while paused. Profiles are still reverted automatically, so nothing stays stuck. |
| **Resume automations** | Ends a pause early. |
| **Profiles** → *name* | Applies the profile, or reverts it if it is active (check mark). |
| **Edit automations file** | Opens `automations.yaml` in your editor for `.yaml` files (Notepad if there is none). |
| **Reload automations** | Reads the file again (normally automatic). |
| **Open log folder** | Opens the agent's log folder. |
| **Exit** | Reverts active profiles and closes the agent. It will not start again until you sign in next time (or you start it from the Start menu / `AutoSettings.Agent.exe`). |

Notifications from the `notify` action and warnings about configuration errors appear as Windows notifications.
Clicking one opens the window.

## Window

### Automations

Lists your automations: **On** (✓ enabled, — disabled, ⏸ suspended by the loop guard), and plain-language **When**,
**If** and **Then** columns.

- **Run now**: checks the conditions and runs the actions now, even if the automation is disabled or AutoSettings is
  paused. Great for testing.
- **Run, ignoring conditions**: runs the actions without checking the conditions.
- **Edit YAML**: jumps to the automation in the editor.
- **Dry run**: while checked, actions are only written to the Activity log, nothing is changed.

### Profiles

Shows each profile, whether it is active, when it will revert, and its settings. **Apply** and **Revert** work like
the tray menu.

### Activity

A timeline of what happened, newest first:

- • something happened (an event, a configuration load),
- ✓ something was done,
- ! something was skipped or partly failed,
- ✗ something failed, with the reason.

It explains *why* things did not happen too: "triggered but condition 2 (power_source: battery) was not met",
"still running", "suspended by the loop guard"...

Switch to **Machine (service) activity** to see what the service did (machine automations, agents connecting).

App start and focus events are only listed when they matter (an automation or a profile reacted to them), so the
timeline stays readable. The full detail is in the log files.

### YAML editor

Edits your `automations.yaml`.

- **Save** checks the whole file first. A file with errors is **not** saved; the problems are listed below the editor.
  Double-click a problem to jump to its line.
- **Check** only validates.
- **Discard changes** reloads the file from disk.
- **Open in another editor** opens the file in your default editor. The app picks up the changes when you save there.

A visual (form-based) editor with a Home Assistant-style YAML view, autocomplete and an app picker is on the
[roadmap](../roadmap.md).

### Status

Shows whether the service is connected, how app starts/exits and focus are detected, who is signed in, where the files
are, and which automations the loop guard has suspended.

## When the service is not running

The agent still works on its own, with some limits:

- focus triggers and personal automations work normally;
- app start/exit is detected by checking the app list every 2 seconds instead of instantly;
- lock/unlock still work; **sign-in** triggers do not (the agent starts after you sign in);
- machine automations do not run.

The Status tab and the Activity log tell you when this is the case.
