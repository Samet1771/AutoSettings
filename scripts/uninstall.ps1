<#
.SYNOPSIS
    Removes the AutoSettings service and program files. Your automations are kept unless -RemoveData is given.
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'AutoSettings'),
    [switch] $RemoveData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'AutoSettings'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated (Run as administrator) PowerShell.'
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}
Get-Process -Name 'AutoSettings.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force

Remove-Item -Path (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\AutoSettings.lnk') -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
}

if ($RemoveData) {
    Remove-Item -Path (Join-Path $env:ProgramData 'AutoSettings') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Machine data removed. Personal automations stay in each user''s %AppData%\AutoSettings folder.'
}
Write-Host 'AutoSettings was removed.'
