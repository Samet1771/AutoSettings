<#
.SYNOPSIS
    Installs AutoSettings from a published folder: registers and starts the Windows Service.
.DESCRIPTION
    Run from an elevated PowerShell in the folder that contains AutoSettings.Service.exe and
    AutoSettings.Agent.exe (the CI artifact, or the output of scripts/publish.ps1).
    The files are copied to "C:\Program Files\AutoSettings", the service is created with automatic
    start, and it starts the agent in every signed-in user's session.
.EXAMPLE
    .\install.ps1
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'AutoSettings'),
    [string] $SourceDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$serviceName = 'AutoSettings'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated (Run as administrator) PowerShell.'
}

if (-not (Test-Path (Join-Path $SourceDir 'AutoSettings.Service.exe'))) {
    throw "AutoSettings.Service.exe was not found in $SourceDir."
}

# Stop an existing installation first.
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'Stopping the existing service...'
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    Get-Process -Name 'AutoSettings.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force
}

Write-Host "Copying files to $InstallDir..."
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
if ((Resolve-Path $SourceDir).Path -ne (Resolve-Path $InstallDir).Path) {
    Copy-Item -Path (Join-Path $SourceDir '*') -Destination $InstallDir -Recurse -Force
}

$exe = Join-Path $InstallDir 'AutoSettings.Service.exe'
if (-not $existing) {
    Write-Host 'Creating the service...'
    New-Service -Name $serviceName -BinaryPathName "`"$exe`"" -DisplayName 'AutoSettings' `
        -Description 'Changes Windows settings automatically when the computer starts, users sign in, and apps start, close or get focus.' `
        -StartupType Automatic | Out-Null
    # Restart the service if it crashes.
    sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null
}

Write-Host 'Starting the service...'
Start-Service -Name $serviceName
Write-Host 'AutoSettings is installed. The tray icon appears in a few seconds.'
