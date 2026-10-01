# restore: put the file back the way read.ps1 saw it. The saved text is in "snapshot" (and $env:AUTOSETTINGS_SNAPSHOT).
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = [Environment]::ExpandEnvironmentVariables($request.parameters.path)
$snapshot = $request.snapshot

if ($snapshot -eq "missing") {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    Write-Output "Removed $path"
} elseif ($snapshot -like "file:*") {
    [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($snapshot.Substring(5)))
    Write-Output "Restored $path"
} else {
    # A non-zero exit code makes the action fail; the error text is shown in the Activity page.
    [Console]::Error.WriteLine("Unknown snapshot: $snapshot")
    exit 1
}
