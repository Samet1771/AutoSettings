# Installation

## Requirements

- Windows 10 version 1809 or later, or Windows 11, 64-bit (x64).
- Administrator rights to install. AutoSettings installs a Windows Service that runs as SYSTEM, so it can see
  the computer start and users sign in.

## Install

### With the installer (recommended)

1. Download `AutoSettings-<version>-x64.msi` from the repository's **Releases** page. Builds of the latest code are
   also on the **Actions** tab: open the latest successful **CI** run and download the **AutoSettings-msi** artifact.
2. Run it. Windows asks for administrator permission.

The installer:

- copies AutoSettings to `C:\Program Files\AutoSettings`,
- registers the **AutoSettings** service with *Automatic* start and restart-on-failure, and starts it,
- adds **AutoSettings** to the Start menu.

The service then starts the agent (the tray icon) in every signed-in user's session. From now on AutoSettings starts
with Windows; you do not need to add anything to the Startup folder.

With winget (once the package is published): `winget install AutoSettings.AutoSettings`.

> The binaries are not code-signed unless the release was built with a signing certificate, so Windows SmartScreen
> may warn the first time. Choose *More info → Run anyway*.

### Without the installer (portable folder)

1. Download the **AutoSettings-win-x64** zip (from a release or the CI artifacts), or build it with
   `scripts/publish.ps1` (see [Building](../dev/building.md)).
2. Unzip it anywhere.
3. Open **PowerShell as administrator** in that folder and run:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\install.ps1
   ```

The script does the same as the installer: it copies the files to `C:\Program Files\AutoSettings`, creates the
service, adds the Start menu shortcut, and starts the service.

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

AutoSettings [updates itself](updates.md) from GitHub: by default it downloads a new version and asks before
installing it. You can also update by hand:

- **Installer**: run the new MSI. It replaces the old version and keeps your automations.
- **Portable**: run `install.ps1` from the new version's folder. It stops the service and the agents, replaces the
  files and starts the service again.

## Uninstall

- **Installer**: Settings → Apps → Installed apps → AutoSettings → Uninstall.
- **Portable**: in an administrator PowerShell:

  ```powershell
  & "C:\Program Files\AutoSettings\uninstall.ps1"             # keeps your automations
  & "C:\Program Files\AutoSettings\uninstall.ps1" -RemoveData  # also removes machine automations and logs
  ```

Your automations are kept either way: machine automations in `%ProgramData%\AutoSettings`, personal automations in
each user's `%AppData%\AutoSettings`. Delete those folders if you do not need them anymore.

## Service settings (`appsettings.json`)

| Setting | Default | Meaning |
|---|---|---|
| `LaunchAgents` | `true` | Start the agent in each user session. Turn off if you start `AutoSettings.Agent.exe` another way. |
| `AgentPath` | *(service folder)* | Location of `AutoSettings.Agent.exe`. |
| `MaxRunsPerMinute` | `20` | Loop guard for machine automations (see [engine](../dev/engine.md#loop-guard)). |
| `LogAllEvents` | `false` | Write every received event to the machine activity log (verbose, for troubleshooting). |

The `Updates` section is described in [Updates](updates.md#company-computers).

The installer replaces this file on every update. To keep your own values, put them in
`%ProgramData%\AutoSettings\appsettings.json` instead (same format, only the settings you change); that file wins
and is never touched by updates.

Restart the service after changing it: `Restart-Service AutoSettings`.
