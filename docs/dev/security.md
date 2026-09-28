# Security model

AutoSettings runs a SYSTEM service that can run commands and change machine settings, so it is designed so that a
normal user cannot use it to gain rights they do not have.

## Configuration files

| File | Who can write | Can contain |
|---|---|---|
| `%ProgramData%\AutoSettings\automations.yaml` | SYSTEM and Administrators (the service sets these permissions on the folder at start; Users can read) | anything, including SYSTEM commands and HKLM changes |
| `%AppData%\AutoSettings\automations.yaml` | the user | only actions that run with the user's own rights |

Validation enforces the second row: machine-only actions (`service.control`, `registry.set` on HKLM,
`command.run` with `run_as: system`) and machine-only triggers (`boot`) are rejected in personal files.

## Named pipe

- Authenticated users may read/write the pipe (so every user's agent can connect); only SYSTEM and administrators
  have full control, so other users cannot create competing server instances.
- The service derives the client's **session from the client process**, and only sends a session's events and actions
  to that session's agent.
- Agents cannot ask the service to run anything: the agent → service API only registers, reads the machine activity
  log, and reports that the user exited.
- The service → agent direction can ask the agent to run **user** actions. The agent re-validates each one as a
  personal action, so even a compromised service message cannot make the agent do more than the user could.
- The agent only talks to a pipe server in session 0 (a service), which prevents another signed-in user from
  squatting the pipe name while the service is stopped and feeding the agent actions.

## Processes

- Agents are started with the user's own token (`WTSQueryUserToken`), which for administrators is the filtered,
  non-elevated token.
- `command.run` scripts are passed to PowerShell with `-EncodedCommand`, so quoting in the script cannot break out
  of the command line.

## Placeholders and scripts

Placeholders are expanded into text fields before an action runs. Their values come from Windows, but some are chosen
by other programs: any program can pick its own window title and file name. Pasting such values into a script would
let a program inject commands, and in a machine automation with `run_as: system` that would let a normal user run
code as SYSTEM.

Therefore placeholders are **never expanded in `command.run` scripts** (the field opts out with
`AllowPlaceholders = false`). The values are passed as environment variables (`AUTOSETTINGS_USER`,
`AUTOSETTINGS_APP_PATH`, ...), which a script reads as data, not code. New action fields that are interpreted as code
must opt out the same way.

## Updates

The service installs updates as SYSTEM, so the update path is treated as privileged:

- Releases are read from `https://api.github.com/repos/<Repository>/releases` over HTTPS. Download addresses must be
  HTTPS on `github.com` or `*.githubusercontent.com`.
- Only an asset named `AutoSettings-<version>-x64.msi` is considered, and only versions higher than the installed one
  (no downgrades). Drafts are ignored; pre-releases only with the opt-in.
- The MSI is downloaded into `%ProgramData%\AutoSettings\updates\<version>`, whose ACL is reset to SYSTEM and
  Administrators (full control) and Users (read). A user therefore cannot replace the file between verification and
  installation.
- Before installing, the SHA-256 of the file must match `SHA256SUMS.txt` of the same release **and** the asset digest
  from the GitHub API when present; at least one of the two is required.
- If the running `AutoSettings.Service.exe` is Authenticode-signed, the MSI must have a valid, trusted signature from
  the same certificate subject. A signed installation never accepts an unsigned update. Unsigned builds rely on the
  checksums and on the integrity of the GitHub repository and account: protect the account with two-factor
  authentication, and consider signing releases (see [releasing](building.md#releasing)).
- Users can only trigger the check and the install of the verified official release, or change the mode; they cannot
  choose the file, the URL or the repository. Administrators can lock the settings with `AllowUserChanges: false`
  in `%ProgramData%\AutoSettings\appsettings.json`.
- `msiexec` is started with an argument list (no shell), with a verbose log next to the download.

## Reporting a vulnerability

Please open a private security advisory on the GitHub repository instead of a public issue.
