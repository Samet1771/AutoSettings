# poll: runs every few seconds. Print one line per event, as JSON or just the event name.
# Remember what you saw in $env:AUTOSETTINGS_STATE_DIR, a folder that is kept between runs.
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
# (AUTOSETTINGS_HELLO_FOLDER lets the automated tests use another folder.)
$folder = if ($env:AUTOSETTINGS_HELLO_FOLDER) { $env:AUTOSETTINGS_HELLO_FOLDER } else { Join-Path $env:USERPROFILE "AutoSettings Hello" }
$stateFile = Join-Path $request.state_directory "files.txt"

$now = @()
if (Test-Path -LiteralPath $folder) {
    $now = @(Get-ChildItem -LiteralPath $folder -File | ForEach-Object { $_.Name })
}
$before = @()
if (Test-Path -LiteralPath $stateFile) {
    $before = @([IO.File]::ReadAllLines($stateFile))
}
[IO.File]::WriteAllLines($stateFile, [string[]]$now)

# The first run after AutoSettings starts only records what is there; files that already exist are not "new".
if ($request.first_poll) { exit 0 }

foreach ($name in $now | Where-Object { $before -notcontains $_ }) {
    @{ event = "example.hello.file_added"; data = @{ name = $name } } | ConvertTo-Json -Compress
}
foreach ($name in $before | Where-Object { $now -notcontains $_ }) {
    @{ event = "example.hello.file_removed"; data = @{ name = $name } } | ConvertTo-Json -Compress
}
