# AutoSettings documentation

AutoSettings changes Windows settings automatically. You describe **rules** ("automations"):

- **When** something happens (a *trigger*): the computer starts, someone signs in, an app starts, closes, gains or loses focus, the screen locks...
- **If** some things are true (*conditions*): it is evening, the laptop is on battery, you are on your home Wi‑Fi...
- **Then** do something (*actions*): switch to dark mode, change the power plan, mute, set the default audio device, run a script...

Groups of settings can be bundled into **profiles** that are applied and **undone automatically**, for example
"gaming mode while Steam is running" or "presentation mode while PowerPoint is focused".

## For users

1. [Installation](guide/installation.md)
2. [Getting started](guide/getting-started.md): your first automation
3. [Writing automations](guide/automations.md): the file format in detail
4. [Profiles](guide/profiles.md): settings that are undone automatically
5. [Personal and machine automations](guide/personal-and-machine.md)
6. [The app](guide/the-app.md): tray icon, activity timeline, editor
7. [Examples](examples/README.md)
8. [Reference](reference/index.md): every trigger, condition and action
9. [Troubleshooting and FAQ](guide/troubleshooting.md)

## For developers

- [Architecture](dev/architecture.md)
- [Rule engine semantics](dev/engine.md)
- [Service ↔ agent protocol](dev/ipc.md)
- [Security model](dev/security.md)
- [Building, testing, releasing](dev/building.md)
- [Adding a new action, condition or trigger](dev/adding-an-action.md)
- [Roadmap](roadmap.md)
