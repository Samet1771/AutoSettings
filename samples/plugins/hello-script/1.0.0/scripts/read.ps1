# capture: print the current state of the file. AutoSettings keeps the text and gives it back to restore.ps1.
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = [Environment]::ExpandEnvironmentVariables($request.parameters.path)

if (Test-Path -LiteralPath $path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    Write-Output ("file:" + [Convert]::ToBase64String($bytes))
} else {
    Write-Output "missing"
}
