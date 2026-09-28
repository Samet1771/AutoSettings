# Personal and machine automations

AutoSettings has two kinds of automation files.

| | Personal automations | Machine automations |
|---|---|---|
| File | `%AppData%\AutoSettings\automations.yaml` | `%ProgramData%\AutoSettings\automations.yaml` |
| Who can edit | you | administrators only |
| Runs in | your agent (tray app), as you | the AutoSettings service, as SYSTEM |
| Applies to | you | every user of the computer |
| Typical use | your own preferences | shared PCs, boot-time tasks, per-user rules set by an admin |

## What each kind can use

| | Personal | Machine |
|---|---|---|
| `boot` trigger | — (you are not signed in yet at boot) | ✓ |
| `logon`, `logoff`, `lock`, `unlock` | ✓ (your own sessions) | ✓ (every user; filter with `user:`) |
| `app_started`, `app_closed` | ✓ (your apps) | ✓ (anyone's apps) |
| `app_focused`, `app_unfocused` | ✓ | — (focus belongs to a user's desktop) |
| `app_focused`, `fullscreen`, `monitor_count` conditions | ✓ | — |
| Actions that change a user's settings (theme, audio, brightness, ...) | ✓ | ✓, sent to the agent of the user the event belongs to |
| `service.control`, `registry.set` on `HKLM`, `command.run` with `run_as: system` | — (needs admin rights) | ✓ |

### How machine automations change user settings

Settings like dark mode, audio devices or the wallpaper belong to a signed-in user. When a machine automation runs such
an action, the service sends it to **the agent in the session of the event**:

- `logon` of *samet* → the action runs in samet's session, as samet.
- `app_started` of an app in session 2 → the action runs in session 2.
- `boot` → there is no user yet, so these actions fail with a clear message. Use `logon` instead.

Machine-only actions (`service.control`, `registry.set` on `HKLM`, `command.run` with `run_as: system`) run in the
service itself.

## Example: a family PC

```yaml
# %ProgramData%\AutoSettings\automations.yaml
version: 1
automations:
  - id: kids-evening
    name: Kids get a bedtime reminder
    triggers:
      - type: logon
        user: [emma, noah]
    conditions:
      - type: time
        after: "20:00"
    actions:
      - type: notify
        message: "It's after 8, {{ user }}. Time to wrap up!"
      - type: display.brightness
        level: 50
```

## Which one should I use?

Use **personal** automations for anything about your own workflow. They are easier to edit and need no admin rights.
Use **machine** automations when the rule is about the computer (boot, services, machine registry) or when an
administrator wants a rule for every user or for specific users.
