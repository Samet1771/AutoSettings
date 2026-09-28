# Profiles

A **profile** is a named set of actions that is applied as a unit and **undone as a unit**. Before a profile changes
a setting, AutoSettings saves the current value; when the profile is reverted, the saved value is restored.

That means you do not need a second "undo" automation for every rule.

```yaml
profiles:
  - id: gaming
    name: Gaming
    priority: 10
    actions:
      - type: power.plan
        plan: high_performance
      - type: audio.default_device
        device: Headphones
      - type: power.sleep_timeout
        minutes: 0
```

| Field | Meaning |
|---|---|
| `id` | Unique id used by `profile.apply`, `profile.revert` and the `profile_active` condition. |
| `name` | Shown in the app and the tray menu. |
| `description` | Notes. |
| `priority` | When active profiles change the same setting, the higher priority wins (default 0). |
| `actions` | The settings. Any action can be used; only *revertible* ones are undone (see below). |

## Applying a profile

### From an automation, reverted automatically

```yaml
automations:
  - id: gaming
    triggers:
      - type: app_started
        app: steam.exe
    actions:
      - type: profile.apply
        profile: gaming          # revert_on: auto (the default)
```

With `revert_on: auto`, the undo is paired with the trigger that applied the profile:

| Applied by | Reverted when |
|---|---|
| `app_focused` | **the same app** loses focus (`app_unfocused`) |
| `app_started` | **the same app** fully closes (last instance, `app_closed`) |
| `logon` | you sign out (`logoff`) |
| `unlock` | the screen is locked (`lock`) |
| anything else | not automatically; use `profile.revert` or the app |

You can choose explicitly with `revert_on: app_unfocused | app_closed | logoff | lock | never`.

### Manually

Right-click the tray icon → **Profiles** → click a profile to apply it (a check mark shows it is active) or click it
again to revert it. The **Profiles** tab has the same buttons. Manually applied profiles stay until you revert them.

### With actions

```yaml
    actions:
      - type: profile.revert
        profile: gaming
```

## Which actions are undone?

Actions marked **revertible** in the [reference](../reference/index.md#actions) are undone: theme, transparency,
wallpaper, taskbar auto-hide, brightness, resolution/refresh rate, power plan, screen and sleep timeouts, volume,
mute, default audio device, radios, mouse speed, registry values and services.

Other actions (launch an app, run a command, notification, open a URL, delay) simply run when the profile is applied.

## Overlapping profiles

Several profiles can be active at once. For each setting, the active profile with the **highest priority** decides
(ties go to the most recently applied one). Example with volume:

| Step | Active profiles | Volume |
|---|---|---|
| start | none | 50 (your own setting) |
| apply *meeting* (priority 0, volume 60) | meeting | 60 |
| apply *gaming* (priority 10, volume 30) | meeting, gaming | 30 |
| revert *meeting* | gaming | 30 (gaming still wins) |
| revert *gaming* | none | **50** (the original value comes back) |

And the other way round: if *gaming* is active and *meeting* (lower priority) is applied, the meeting volume is not
applied at all. It takes effect when *gaming* is reverted, as long as *meeting* is still active.

"The same setting" means the same action type **and** the same target: `audio.volume` for *Headphones* and
`audio.volume` for *Speakers* are different settings, as are `theme.mode` with `target: apps` and `target: system`.

## Good to know

- Pausing AutoSettings stops new automations, but automatic reverts still happen, so nothing gets stuck.
- When you exit the agent from the tray, active profiles are reverted first.
- If a setting cannot be read before it is changed (for example the device is unplugged), a warning is logged and
  that setting is not restored.
- If you change a setting by hand while a profile is active, reverting the profile still restores the value from
  **before** the profile was applied.
- Profiles cannot apply other profiles.
