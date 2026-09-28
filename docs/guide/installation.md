# Installation

## Requirements

- Windows 10 version 1809 or later, or Windows 11, 64-bit (x64).
- Administrator rights to install. AutoSettings installs a Windows Service that runs as SYSTEM, so it can see
  the computer start and users sign in.

## Install

> An MSI installer is on the [roadmap](../roadmap.md). Until then, use the published folder and the install script.

1. Download the **AutoSettings-win-x64** build:
   open the repository's **Actions** tab, pick the latest successful **CI** run, and download the artifact
   (or build it yourself with `scripts/publish.ps1`, see [Building](../dev/building.md)).
2. Unzip it anywhere.
3. Open **PowerShell as administrator** in that folder and run:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\install.ps1
   ```

The script:

- copies the files to `C:\Program Files\AutoSettings`,
- creates the **AutoSettings** service with *Automatic* start and restart-on-failure,
- adds an **AutoSettings** shortcut to the Start menu,
- starts it. The service then starts the agent (the tray icon) in every signed-in user's session.

From now on AutoSettings starts with Windows. You do not need to add anything to the Startup folder.

## Where things are

| What | Where |
|---|---|
| Program files | `C:\Program Files\AutoSettings` |
| Your automations | `%AppData%\AutoSettings\automations.yaml` (created with disabled examples on first start) |
| Machine automations | `%ProgramData%\AutoSettings\automations.yaml` (only administrators can edit it) |
| Service log | `%ProgramData%\AutoSettings\logs\service-*.log` |
| Agent log (per user) | `%LocalAppData%\AutoSettings\logs\agent-*.log` |
| Service settings | `C:\Program Files\AutoSettings\appsettings.json` |

## Update

Run `install.ps1` from the new version's folder. It stops the service and the agents, replaces the files and starts
the service again. Your automations are not touched.

## Uninstall

In an administrator PowerShell:

```powershell
& "C:\Program Files\AutoSettings\uninstall.ps1"            # keeps your automations
& "C:\Program Files\AutoSettings\uninstall.ps1" -RemoveData  # also removes machine automations and logs
```

Personal automations stay in each user's `%AppData%\AutoSettings` folder; delete it if you do not need it anymore.

## Service settings (`appsettings.json`)

| Setting | Default | Meaning |
|---|---|---|
| `LaunchAgents` | `true` | Start the agent in each user session. Turn off if you start `AutoSettings.Agent.exe` another way. |
| `AgentPath` | *(service folder)* | Location of `AutoSettings.Agent.exe`. |
| `MaxRunsPerMinute` | `20` | Loop guard for machine automations (see [engine](../dev/engine.md#loop-guard)). |
| `LogAllEvents` | `false` | Write every received event to the machine activity log (verbose, for troubleshooting). |

Restart the service after changing it: `Restart-Service AutoSettings`.
