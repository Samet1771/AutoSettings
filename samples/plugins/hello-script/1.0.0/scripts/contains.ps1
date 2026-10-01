# evaluate: the last line printed must be true or false.
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = [Environment]::ExpandEnvironmentVariables($request.parameters.path)

if ((Test-Path -LiteralPath $path) -and ((Get-Content -LiteralPath $path -Raw) -like "*$($request.parameters.text)*")) {
    "true"
} else {
    "false"
}
