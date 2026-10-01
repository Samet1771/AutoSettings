# Writing a script plugin

A script plugin is a folder with a [`plugin.yaml`](manifest.md) and some PowerShell files. There is nothing to compile.
This page builds a plugin step by step. The complete
[hello-script sample](https://github.com/Samet1771/AutoSettings/tree/main/samples/plugins/hello-script) has a
revertible action, a condition and two triggers.

## 1. The folder

```text
%LocalAppData%\AutoSettings\plugins\        ← your personal plugins
  example.hello\                            ← the plugin id
    1.0.0\                                  ← the version
      plugin.yaml
      scripts\write.ps1
      scripts\read.ps1
      ...
```

The folder names must match `id` and `version` in `plugin.yaml`. AutoSettings watches the plugins folder. A few
seconds after you copy a plugin in, change a file or delete it, the Activity page says what was loaded. Copying a
folder by hand is how you develop and test a plugin; users install a packaged plugin with the app instead.

Machine plugins (`scope: machine`) go to `%ProgramData%\AutoSettings\plugins` instead, which only administrators can
change. The service refuses to run plugins from a folder that other users can change.

## 2. The manifest

```yaml
id: example.hello
name: Hello
version: 1.0.0
publisher: Me
kind: script
sdk: "1.0"
components:
  - type: example.hello.write_text
    kind: action
    title: Write a text file
    description: Writes text to a file.
    scripts:
      apply: scripts/write.ps1
    fields:
      - name: path
        type: path
        description: The file.
        required: true
      - name: text
        type: multiline
        description: What to write.
        required: true
```

Every key is described in the [plugin.yaml reference](manifest.md).

## 3. The scripts

Every time AutoSettings needs the plugin, it runs one script with Windows PowerShell 5.1. It runs in the plugin
folder, with `-NoProfile -NonInteractive -ExecutionPolicy Bypass`, with no window, and as the user. Machine actions
of machine plugins run as SYSTEM.

### Input

The request arrives as **JSON on standard input**:

```powershell
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = [Environment]::ExpandEnvironmentVariables($request.parameters.path)
```

```json
{
  "operation": "apply",
  "component": "example.hello.write_text",
  "plugin": { "id": "example.hello", "version": "1.0.0", "directory": "C:\\...\\1.0.0", "scope": "user" },
  "parameters": { "path": "%USERPROFILE%\\Desktop\\status.txt", "text": "Hello samet", "continue_on_error": false },
  "event": {
    "name": "app_focused", "session": 1,
    "user": { "name": "samet", "domain": "PC", "sid": "S-1-5-21-..." },
    "app": "code.exe", "app_path": "C:\\...\\Code.exe", "window_title": "...",
    "data": {}
  },
  "user": { "name": "samet", "domain": "PC", "sid": "S-1-5-21-..." },
  "automation": "Focus mode",
  "state_directory": "C:\\Users\\samet\\AppData\\Local\\AutoSettings\\plugin-data\\example.hello"
}
```

- `parameters` holds every field with its default filled in. Numbers and booleans are JSON numbers and booleans,
  lists are arrays, and durations and times are text (`30s`, `22:00`).
- In text fields, `{{ placeholders }}` are already replaced. A field with `placeholders: false` arrives as written.
- `event` describes what started the automation; it is missing for manual runs. For plugin events, `data` holds the
  values the plugin sent.
- `snapshot` is present for `restore` (see below). `first_poll` is present for `poll`.

The same values are also in **environment variables**, which is handy for short scripts:

| Variable | Value |
|---|---|
| `AUTOSETTINGS_OPERATION` | `apply`, `capture`, `restore`, `evaluate` or `poll` |
| `AUTOSETTINGS_PARAM_<NAME>` | each parameter as text, for example `$env:AUTOSETTINGS_PARAM_PATH`. Lists are comma-separated |
| `AUTOSETTINGS_SNAPSHOT` | for `restore`: the saved value |
| `AUTOSETTINGS_STATE_DIR` | a folder the plugin may keep files in; it is kept between runs |
| `AUTOSETTINGS_PLUGIN_DIR` | the plugin's folder |
| `AUTOSETTINGS_FIRST_POLL` | for `poll`: `1` on the first run after AutoSettings starts, else `0` |
| `AUTOSETTINGS_USER`, `AUTOSETTINGS_APP`, `AUTOSETTINGS_EVENT_DATA_<NAME>`, … | the [placeholders](../reference/placeholders.md) |

!!! warning "Never run parameters as code"
    Values come from automations and from events (window titles, file names). Use them as data: pass them to
    cmdlets as arguments (`-LiteralPath $path`), never paste them into `Invoke-Expression` or a command line.

### Output and errors

- Everything a script prints to standard output is UTF-8 text.
- **Exit code 0** means success. `exit 1` (any non-zero code) or an uncaught `throw` means failure. The end of the
  error output (or of the normal output) is shown in the Activity page.
- What each operation prints is described below.

## Actions

| Script | When | Prints |
|---|---|---|
| `apply` | the action runs | anything; it is shown in the Activity page |
| `capture` | before a profile changes the setting for the first time (revertible actions only) | the current value, as text in any format you like |
| `restore` | when the profile ends | anything |

`capture` and `restore` make an action **revertible**. Set `revertible: true` and mark the field that says *which*
setting changes with `key: true` (for example the file path). AutoSettings keeps the text `capture` printed and gives
it back to `restore` as `snapshot`:

```powershell
# read.ps1 (capture)
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = [Environment]::ExpandEnvironmentVariables($request.parameters.path)
if (Test-Path -LiteralPath $path) { "file:" + [Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) } else { "missing" }
```

```powershell
# restore.ps1
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = [Environment]::ExpandEnvironmentVariables($request.parameters.path)
if ($request.snapshot -eq "missing") { Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue }
else { [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($request.snapshot.Substring(5))) }
```

## Conditions

The `evaluate` script prints `true` or `false` (any case) as its **last line**. Anything else, or a failure, counts as
false and is noted in the Activity page. Keep conditions fast: they run every time an automation is triggered.

```powershell
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
(Test-Path -LiteralPath $request.parameters.path).ToString()
```

## Triggers

A script trigger has a `poll` script that runs every `interval` (default `30s`, at least `5s`). It prints **one line
per event**, either just the event name:

```text
example.hello.file_added
```

or a JSON object with values:

```powershell
@{ event = "example.hello.file_added"; data = @{ name = $name }; user = "samet" } | ConvertTo-Json -Compress
```

- Event names must start with the plugin id. Other lines are ignored and noted in the Activity page.
- `data` values become `{{ event.data.<name> }}` and filter trigger fields of the same name.
- `user` is optional: the user the event is about. It defaults to the user the plugin runs for.

Polling finds *changes*, so the script must remember what it saw last time. Keep that in `state_directory`. On the
first run after AutoSettings starts (`first_poll` is true), record the current state and print nothing, so things that
already exist are not reported as new:

```powershell
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$stateFile = Join-Path $request.state_directory "files.txt"
$now = @(Get-ChildItem -LiteralPath $folder -File | ForEach-Object Name)
$before = if (Test-Path $stateFile) { @([IO.File]::ReadAllLines($stateFile)) } else { @() }
[IO.File]::WriteAllLines($stateFile, [string[]]$now)
if ($request.first_poll) { exit 0 }
foreach ($name in $now | Where-Object { $before -notcontains $_ }) {
    @{ event = "example.hello.file_added"; data = @{ name = $name } } | ConvertTo-Json -Compress
}
```

One poll script can raise several events, for example `file_added` and `file_removed`. Declare each event as a
trigger component; only one of them needs the `poll` script. Set `opposite` so profiles applied by one event are
reverted by the other.

AutoSettings only polls while an automation uses one of the plugin's triggers. Triggers run where the automation
runs: personal automations poll as the user in their app, and machine automations poll as SYSTEM in the service.

## Testing your plugin

1. Copy the plugin to `%LocalAppData%\AutoSettings\plugins\<id>\<version>` and watch the Activity page. Problems in
   `plugin.yaml` are reported with their line numbers.
2. Run a script by hand with a request to check its output:

   ```powershell
   '{"operation":"apply","parameters":{"path":"C:\\temp\\a.txt","text":"hi"},"state_directory":"C:\\temp"}' |
       powershell -NoProfile -File .\scripts\write.ps1
   ```

3. Use the component in an automation. With **Test actions** in the editor you can run the actions without waiting
   for the trigger.

## Limits

- Each run starts a new PowerShell process, which takes about half a second. For events that need an instant reaction
  or a long-running watcher, write a [.NET plugin](index.md#two-kinds-of-plugins).
- A run is stopped after `timeout` (default 60 seconds).
- Scripts get no window and cannot ask the user anything.
