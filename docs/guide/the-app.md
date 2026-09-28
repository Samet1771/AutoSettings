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
| **Exit** | Reverts active profiles and closes the agent. It is not started again until your next sign-in, or until you open **AutoSettings** from the Start menu. |

Notifications from the `notify` action and warnings about configuration errors appear as Windows notifications.
Clicking one opens the window.

## Window

The window follows the Windows light/dark theme, or the theme you choose in **Settings**. The sidebar has five
pages. At the bottom it shows the current state (automations on, paused, active profiles, service connection) and a
**Pause for 1 hour / Resume** button.

### Automations

Lists your automations, with plain-language **When**, **If** and **Then** columns and a switch to turn each one on
or off. ⏸ after a name means the loop guard suspended it.

The toolbar has **New**, **Edit**, **Duplicate**, **Delete**, **↑/↓**, **Run now**, **Run, ignoring conditions**,
**Import**, **Export** and **Edit whole file**. See [The editor](editor.md).

### Profiles

Shows each profile, whether it is active and when it will revert, its priority and its settings. The toolbar has
**New**, **Edit**, **Duplicate**, **Delete**, **Apply** and **Revert**.

### Templates

Ready-made automations you can add with one click. See [Templates](editor.md#templates).

### Activity

A timeline of what happened, newest first:

- • something happened (an event, a configuration load),
- ✓ something was done,
- ! something was skipped or partly failed,
- ✗ something failed, with the reason.

It also explains *why* things did not happen: "triggered but condition 2 (power_source: battery) was not met",
"still running", "suspended by the loop guard"...

Switch to **Machine (service) activity** to see what the service did (machine automations, agents connecting).

App start and focus events are only listed when they matter (an automation or a profile reacted to them), so the
timeline stays readable. The full detail is in the log files.

### Settings

- **Language**: English, Türkçe, or the same as Windows. Takes effect after restarting the agent.
- **Theme**: light, dark, or the same as Windows.
- **Dry run**: while on, actions are only written to the Activity page, nothing is changed.
- **Status**: whether the service is connected, how apps and focus are detected, who is signed in, where the files
  are, and which automations the loop guard has suspended.
- Buttons to edit the whole file, open the automations and log folders, and open this documentation.

## When the service is not running

The agent still works on its own, with some limits:

- focus triggers and personal automations work normally;
- app start/exit is detected by checking the app list every 2 seconds instead of instantly;
- lock/unlock still work; **sign-in** triggers do not (the agent starts after you sign in);
- machine automations do not run.

The Settings page and the Activity log tell you when this is the case.
