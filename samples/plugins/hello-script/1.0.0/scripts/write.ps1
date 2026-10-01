# apply: write the text to the file.
# The request (operation, parameters, event, user...) arrives as JSON on standard input.
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = [Environment]::ExpandEnvironmentVariables($request.parameters.path)

$folder = Split-Path -Parent $path
if ($folder -and -not (Test-Path -LiteralPath $folder)) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}
Set-Content -LiteralPath $path -Value $request.parameters.text -Encoding UTF8 -NoNewline

# Anything printed is shown in the Activity page.
Write-Output "Wrote $path"
