# Building, testing, releasing

## Prerequisites

- Windows 10/11 x64 for running the app. Core, the tests and DocGen also build and run on Linux/macOS.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json`).
- Any editor; Visual Studio 2026, Rider and VS Code (C# Dev Kit) all open `AutoSettings.slnx`.

## Build and test

```powershell
dotnet build AutoSettings.slnx
dotnet test  AutoSettings.slnx
```

## Run during development

The service can run as a normal console app. It then cannot see session changes (logon/lock), cannot start agents,
and uses WMI/polling for processes unless the console is elevated:

```powershell
# Terminal 1, as administrator for ETW process events:
dotnet run --project src/AutoSettings.Service

# Terminal 2, as yourself:
dotnet run --project src/AutoSettings.Agent
```

To test everything including boot, logon and agent launch, install a local build as a real service:

```powershell
.\scripts\publish.ps1                       # creates artifacts\AutoSettings
cd artifacts\AutoSettings
.\install.ps1                               # as administrator
```

Logs: `%ProgramData%\AutoSettings\logs` (service) and `%LocalAppData%\AutoSettings\logs` (agent).

## Reference docs

`docs/reference` is generated from the component catalog. After changing a trigger, condition or action:

```powershell
dotnet run --project src/AutoSettings.DocGen            # rewrite docs/reference
dotnet run --project src/AutoSettings.DocGen -- --check # what CI runs: fails if the files are out of date
```

No .NET SDK at hand? Run the **Regenerate reference docs** workflow (Actions tab → *Run workflow* on your branch);
it commits the regenerated files to the branch.

## Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request:

| Job | Runner | Steps |
|---|---|---|
| Build, test & publish | `windows-latest` | restore, build (Release), test, publish self-contained `win-x64` service + agent + scripts, upload the `AutoSettings-win-x64` artifact |
| Reference docs up to date | `ubuntu-latest` | `DocGen --check` |

## Manual test checklist

Unit tests cover the engine; the Windows integration needs a real machine or VM. Before a release:

1. Install with `install.ps1`, restart the computer.
   *Machine activity*: "Computer started" once. Restart the service: no second boot event.
2. Shut down (Fast Startup on) and start again: "Computer started (Fast Startup)".
3. Sign in as two different users. Each gets a tray icon; each logon event shows the right user name.
4. Lock and unlock: events in the personal activity.
5. Start and close Notepad: *app_started* / *app_closed* fire once; with Chrome (many processes) also only once.
6. Alt+Tab between two apps: *app_focused* / *app_unfocused* fire without flicker.
7. Rule "Notepad focused → dark mode" (profile): dark while focused, back to light when leaving.
8. Stop the service: the agent warns and falls back to polling; start it again: the agent reconnects.
9. Exit the agent from the tray: it is not restarted; sign out and in: it starts again.
10. Kill the agent in Task Manager: the service restarts it within seconds.
11. Save a broken YAML file: previous automations keep running, the error is shown with its line number.

## Releasing

1. Update `CHANGELOG.md` and `<Version>` in `Directory.Build.props`.
2. Tag `vX.Y.Z` and push. Download the CI artifact of that commit and attach it to a GitHub release.
   (An MSI and a release workflow are on the [roadmap](../roadmap.md).)
