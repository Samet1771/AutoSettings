<#
.SYNOPSIS
    Builds a self-contained AutoSettings folder (service + agent + install scripts) in artifacts\AutoSettings.
#>
[CmdletBinding()]
param([string] $Output = (Join-Path $PSScriptRoot '..\artifacts\AutoSettings'))

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
dotnet publish (Join-Path $root 'src/AutoSettings.Service') -c Release -r win-x64 --self-contained -o $Output
dotnet publish (Join-Path $root 'src/AutoSettings.Agent') -c Release -r win-x64 --self-contained -o $Output
Copy-Item (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1') $Output
Write-Host "Published to $Output. Run install.ps1 from there as administrator."
