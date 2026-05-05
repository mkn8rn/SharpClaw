<#
.SYNOPSIS
    Removes SharpClaw API/Gateway Windows Services registered by install-service.ps1.
#>
param(
    [string]$ApiServiceName     = "SharpClawApi",
    [string]$GatewayServiceName = "SharpClawGateway"
)

$ErrorActionPreference = "Stop"

function Assert-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = [Security.Principal.WindowsPrincipal]::new($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Error "This script must be run from an elevated PowerShell prompt."
        exit 1
    }
}

function Remove-IfPresent {
    param([string]$Name)
    if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
        Write-Host "Removing $Name ..." -ForegroundColor Yellow
        sc.exe stop $Name | Out-Null
        Start-Sleep -Seconds 1
        sc.exe delete $Name | Out-Null
        Write-Host "[OK] Removed $Name" -ForegroundColor Green
    } else {
        Write-Host "Service '$Name' not present." -ForegroundColor DarkGray
    }
}

Assert-Elevated
Remove-IfPresent $GatewayServiceName
Remove-IfPresent $ApiServiceName
