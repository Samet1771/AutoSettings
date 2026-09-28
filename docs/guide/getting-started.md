# Getting started

This walks you through your first automation: **dark mode in the evening** and a **presentation mode** that is
undone automatically.

## 1. Open AutoSettings

After [installation](installation.md) the AutoSettings icon (a blue square with a switch) is in the notification area,
next to the clock. It may be hidden under the **^** arrow; drag it onto the taskbar to keep it visible.

Double-click it to open the window. You will see these tabs:

- **Automations**: your rules, with a plain-language summary of when they run and what they do.
- **Profiles**: groups of settings you can apply and revert.
- **Activity**: what happened and why, newest first.
- **YAML editor**: where you write automations.
- **Status**: whether everything is connected, and where files are.

## 2. Look at the starter file

On first start AutoSettings creates `%AppData%\AutoSettings\automations.yaml` with a few **disabled** examples.
Open the **YAML editor** tab. An automation looks like this:

```yaml
automations:
  - id: welcome                     # a unique name, used internally
    name: Say hello when I sign in  # what you see in the app
    enabled: false                  # remove this line (or set true) to turn it on
    triggers:                       # WHEN any of these happens...
      - type: logon
    actions:                        # ...THEN do these, in order
      - type: notify
        message: "Welcome back, {{ user }}!"
```

Indentation matters in YAML: use spaces (not tabs), and keep items of a list aligned.

## 3. Your first automation: dark mode in the evening

Add this under `automations:` (keep the two-space indentation of the other items):

```yaml
  - id: evening-dark-mode
    name: Dark mode in the evening
    triggers:
      - type: logon
      - type: unlock
    conditions:
      - type: time
        after: "19:00"
        before: "07:00"
    actions:
      - type: theme.mode
        mode: dark
```

Click **Save**. If there is a mistake, the list under the editor shows the line and what is wrong. Double-click an
entry to jump to it. A file with errors is never saved, and the previous automations keep running.

Now lock the screen (Win+L) and unlock it. If it is after 19:00, Windows switches to dark mode. The **Activity** tab
shows what happened:

```
• Screen unlocked (PC\samet).
• ▶ 'Dark mode in the evening' started: screen unlocked (PC\samet).
✓ theme.mode (mode: dark) done.
✓ 'Dark mode in the evening' finished.
```

If it is not evening, you will see *"...was triggered (screen unlocked) but condition 1 (time ...) was not met"*.
The Activity tab always tells you why something did or did not run.

**Tip:** select the automation in the **Automations** tab and click **Run, ignoring conditions** to try the actions
right away.

## 4. A profile that undoes itself

A *profile* is a set of settings that is applied together and **reverted together**: AutoSettings remembers the old
values and puts them back.

```yaml
automations:
  - id: presentation-mode
    name: Presentation mode while PowerPoint is focused
    triggers:
      - type: app_focused
        app: POWERPNT.EXE
    actions:
      - type: profile.apply
        profile: presentation

profiles:
  - id: presentation
    name: Presentation
    actions:
      - type: audio.mute
        state: mute
      - type: power.screen_timeout
        minutes: 0
```

When PowerPoint becomes the active window, sound is muted and the screen stays on. When you switch to another app,
the previous volume state and screen timeout come back. Switch back to PowerPoint and they are applied again.

Read more in [Profiles](profiles.md).

## 5. Next steps

- Browse the [examples](../examples/README.md) (gaming mode, meetings, day/night, company PCs).
- Learn the [file format](automations.md) and the [reference](../reference/index.md) of everything you can use.
- Use **Pause** in the tray menu when you want AutoSettings to leave your settings alone for a while.
