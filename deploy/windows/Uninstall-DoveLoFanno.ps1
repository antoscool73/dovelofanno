#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Toglie il servizio DoveLoFanno e il programma. La cartella dati e le registrazioni restano,
    a meno di -RemoveData (che toglie database, backup e log, non le registrazioni).
#>
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'DoveLoFanno'),
    [string]$DataDir = (Join-Path $env:ProgramData 'DoveLoFanno'),
    [switch]$RemoveData,
    # i file del programma li toglie il disinstallatore (che sta girando da quella cartella)
    [switch]$KeepFiles,
    [string]$Log = ''
)
$ErrorActionPreference = 'Stop'
$ServiceName = 'DoveLoFanno'
if ($Log) { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null; Start-Transcript -LiteralPath $Log -Append | Out-Null }

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60)) }
    & sc.exe delete $ServiceName | Out-Null
}
Get-NetFirewallRule -DisplayName 'DoveLoFanno' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if (-not $KeepFiles -and (Test-Path -LiteralPath $InstallDir)) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
if ($RemoveData -and (Test-Path -LiteralPath $DataDir)) {
    foreach ($n in 'dlf.db', 'dlf.db-wal', 'dlf.db-shm', 'backup', 'logs') {
        $p = Join-Path $DataDir $n
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    }
    Get-ChildItem -LiteralPath $DataDir -Filter 'dlf.db.v*.bak' | Remove-Item -Force
}
Write-Host 'DoveLoFanno disinstallato.'
if ($Log) { Stop-Transcript | Out-Null }
