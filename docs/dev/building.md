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

No .NET SDK at hand? On feature branches the **Regenerate reference docs** workflow runs automatically when the
catalog or DocGen changes and commits the regenerated files to the branch (pull afterwards). It can also be started
from the Actions tab.

## Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request:

| Job | Runner | Steps |
|---|---|---|
| Build, test & publish | `windows-latest` | restore, build (Release), test, publish self-contained `win-x64` service + agent + scripts, upload the `AutoSettings-win-x64` artifact |
| Reference docs up to date | `ubuntu-latest` | `DocGen --check` |
| Docs site | `ubuntu-latest` | `mkdocs build --strict` |

The Windows job also builds the MSI and uploads it as the `AutoSettings-msi` artifact.

## MSI installer

The installer project is `installer/AutoSettings.Installer` (WiX 5, restored from NuGet; no separate WiX install
needed). It packages a publish folder:

```powershell
.\scripts\publish.ps1
dotnet build installer/AutoSettings.Installer -c Release -p:PublishDir=$PWD\artifacts\AutoSettings\ -p:ProductVersion=0.2.0
# -> installer/AutoSettings.Installer/bin/x64/Release/AutoSettings-x64.msi
```

The MSI installs to `C:\Program Files\AutoSettings`, registers the service (automatic start, restarts on failure),
adds a Start menu shortcut and upgrades older versions in place (fixed `UpgradeCode`). Uninstall stops the service
and closes the agents; automation files in `%ProgramData%` and `%AppData%` stay.

## Translations

The UI texts live in `tools/strings.py` (English and Turkish side by side). Edit that file and run
`python3 tools/strings.py` to regenerate the `.resx` files in `src/AutoSettings.Agent/Resources`.
`LocalizationTests` checks that both languages have the same keys and that every component has a Turkish title.

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
12. Editor: create an automation in the visual view, switch to YAML and back; nothing changes. Break the YAML: the
    visual tab stays locked, squiggles and the issue list point to the line. Autocomplete after `type:` and for fields.
13. App picker and user picker list the right items; Browse fills the path.
14. Add every template, save, and check they validate. Import and export a file with clashing ids.
15. Switch the language to Turkish and the theme to dark and light; restart the agent: the choice is kept.
16. New actions on real hardware: HDR on/off, main display, scaling 125 %, Night light, accent color, power mode,
    notification banners, Do Not Disturb, keyboard layout, airplane mode, `settings.open`; each reverts with a profile.
17. MSI: install on a clean VM (service running, tray icon after sign-in, Start menu entry), install a newer version
    over it (upgrade, automations kept), uninstall (service removed, `%ProgramData%\AutoSettings` kept).
18. Updates: publish `v0.2.0` and then `v0.2.1` (or a test repository set in
    `%ProgramData%\AutoSettings\appsettings.json`). Install 0.2.0; **Settings → Updates → Check now** shows 0.2.1
    and a notification; **Install 0.2.1**: the app closes and comes back, Activity says *updated from 0.2.0 to
    0.2.1*. Repeat with *Install automatically* while a full-screen app runs (waits) and after closing it.
    Edit `SHA256SUMS.txt` of a test release: the download is rejected. A portable install only notifies.

## Branches

| Branch | Purpose |
|---|---|
| `beta` | Every change lands here first, through a pull request. Each merge can be released as a beta (pre-release). |
| `main` | Only what was tested as a beta. `beta` is merged into `main` for a stable release. |

Pull requests go to `beta`, never directly to `main`. Feature branches are started from `beta`.

## Releasing

Betas and stable releases use the same workflow; only the tag differs.

| Release | Branch | Tag | Who gets it |
|---|---|---|---|
| Beta | `beta` | `v0.2.0-beta.1`, `v0.2.0-beta.2`, … | Users who turned on *Include beta versions* |
| Stable | `main` | `v0.2.0` | Everyone |

`<Version>` in `Directory.Build.props` is the next stable version (for example `0.2.0`) while its betas are
released. After a stable release, raise it: the last number for fixes (`0.2.1`), the middle one for new features
(`0.3.0`).

To promote a beta: open a pull request from `beta` to `main`, merge it, and release `vX.Y.Z` from `main`.

1. Update `CHANGELOG.md` and `<Version>` in `Directory.Build.props`, and merge to the branch you release from.
2. Create the release, in either way:
   - **On the website**: *Releases → Draft a new release → Choose a tag*, type a **new** tag such as `v0.2.0`
     (target `beta` for a beta, `main` for a stable release), write a title and notes, tick *Set as a pre-release* for betas, and **Publish**.
   - **From the command line**: `git tag v0.2.0` and `git push origin v0.2.0`. The release is then created with a
     generated title and notes.

   The tag must be `v` followed by a version (`v0.2.0`, `v0.3.0-beta.1`), higher than the previous release. Other
   tags (for example `beta`) stop the workflow with an error: delete that release **and** its tag (*Tags → the tag →
   Delete*), then create it again. Do not attach files yourself.
3. `.github/workflows/release.yml` runs (Actions tab, about 5 minutes) and:
   - runs the tests, publishes, and signs the executables and the MSI when the `SIGNING_CERT` (base64 .pfx) and
     `SIGNING_PASSWORD` secrets exist;
   - builds `AutoSettings-X.Y.Z-x64.msi` and the portable `AutoSettings-X.Y.Z-win-x64.zip`;
   - fills the winget manifests from `packaging/winget` with the version, URL and SHA256;
   - writes `SHA256SUMS.txt`, which the app's updater requires;
   - attaches all of these to the release. A release made on the website keeps its title, notes and pre-release
     box; for a pushed tag, labels (`v0.3.0-beta.1`) make it a pre-release.

   If it failed, fix the cause and start it again from *Actions → Release → Run workflow* with the same tag.
4. Installed copies find the release within about 12 hours (see [updates](../guide/updates.md)). Pre-releases are
   only offered to users who opted in to beta versions. Keep the asset names: the updater looks for
   `AutoSettings-<version>-x64.msi` and `SHA256SUMS.txt`.
5. Submit the winget manifests to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)
   (for example with `wingetcreate submit`).

### Documentation site

`.github/workflows/docs-pages.yml` publishes the MkDocs site to GitHub Pages on pushes to `main`. Enable it once:
**Settings → Pages → Source: GitHub Actions**, then add the repository variable `PAGES_ENABLED` = `true`
(**Settings → Secrets and variables → Actions → Variables**).
