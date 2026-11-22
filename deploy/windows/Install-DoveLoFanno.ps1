#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installa o aggiorna DoveLoFanno come servizio Windows (installazione "solo un PC").

.DESCRIPTION
    Copia il programma in -InstallDir, crea (o aggiorna) il servizio "DoveLoFanno" con avvio automatico e
    riavvio in caso di errore, apre la porta nel firewall solo per le reti private e avvia il servizio.
    Un aggiornamento ferma il servizio, sostituisce i file del programma e lo riavvia: la cartella dati
    (database, backup, log) e le registrazioni non si toccano, e il database si aggiorna da solo all'avvio
    dopo averne fatto una copia (dlf.db.vN.bak).
    Il servizio gira come LocalSystem: ffmpeg deve stare in un percorso che vede anche lui, quindi lo script
    lo cerca ora (PATH dell'utente, winget) e ne salva il percorso nella configurazione del servizio.
    Gira con Windows PowerShell 5.1.

    L'installatore (DoveLoFanno-<versione>-setup.exe) copia lui i file e chiama questo script con -NoCopy.

.EXAMPLE
    .\Install-DoveLoFanno.ps1 -Package .\DoveLoFanno-win-x64 -RecordingsDir "D:\Registrazioni"
#>
param(
    [Parameter(Mandatory)] [string]$Package,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'DoveLoFanno'),
    [string]$DataDir = (Join-Path $env:ProgramData 'DoveLoFanno'),
    [string]$RecordingsDir = '',
    [string]$Ffmpeg = '',
    [int]$Port = 8765,
    [switch]$NoFirewall,
    # i file del programma sono già in -InstallDir (li ha messi l'installatore): solo configurazione e servizio
    [switch]$NoCopy,
    # registro di quello che succede (l'installatore lo scrive in ProgramData\DoveLoFanno\logs)
    [string]$Log = ''
)
$ErrorActionPreference = 'Stop'
$ServiceName = 'DoveLoFanno'
$Exe = 'DoveLoFanno.Server.exe'
if ($Log) { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null; Start-Transcript -LiteralPath $Log -Append | Out-Null }

if (-not (Test-Path -LiteralPath (Join-Path $Package $Exe))) { throw "$Exe non trovato in $Package" }
if (-not $RecordingsDir) { $RecordingsDir = Join-Path $DataDir 'registrazioni' }
# senza i runtime condivisi il servizio esce subito e Windows dice solo "Impossibile avviare il servizio"
foreach ($fw in 'Microsoft.NETCore.App', 'Microsoft.AspNetCore.App') {
    $fwDir = Join-Path $env:ProgramFiles "dotnet\shared\$fw"
    if (-not (Get-ChildItem -LiteralPath $fwDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^10\.' })) {
        throw "manca il runtime $fw 10 di Microsoft (https://dotnet.microsoft.com/download/dotnet/10.0)"
    }
}

if (-not $Ffmpeg) {
    $cmd = Get-Command ffmpeg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    # winget mette i collegamenti in Programmi\WinGet\Links (installazione per tutto il PC) o nel profilo dell'utente;
    # appena installato, il PATH di questo processo non li vede ancora
    $links = @((Join-Path $env:ProgramFiles 'WinGet\Links\ffmpeg.exe'), (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\ffmpeg.exe'))
    if ($cmd) { $Ffmpeg = $cmd.Path }
    else { $Ffmpeg = $links | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
}
# i collegamenti di winget puntano al vero eseguibile nella cartella del pacchetto: LocalSystem deve vedere quello
if ($Ffmpeg) {
    $item = Get-Item -LiteralPath $Ffmpeg
    if ($item.LinkType -and $item.Target) { $Ffmpeg = @($item.Target)[0] }
}
if (-not $Ffmpeg -or -not (Test-Path -LiteralPath $Ffmpeg)) { throw 'ffmpeg non trovato: installarlo (winget install Gyan.FFmpeg) o indicarlo con -Ffmpeg' }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Aggiornamento: fermo il servizio"
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; (Get-Service $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60)) }
}

Write-Host "Programma in $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir, $DataDir, $RecordingsDir | Out-Null
if (-not $NoCopy) {
    # i file vecchi del programma si tolgono (non la configurazione di questa installazione)
    Get-ChildItem -LiteralPath $InstallDir -Force | Where-Object { $_.Name -ne 'appsettings.Production.json' } | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $Package '*') -Destination $InstallDir -Recurse -Force
}

$config = [ordered]@{
    Urls        = "http://0.0.0.0:$Port"
    DoveLoFanno = [ordered]@{ DataDir = $DataDir; RecordingsDir = $RecordingsDir; Ffmpeg = $Ffmpeg }
}
$json = $config | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $InstallDir 'appsettings.Production.json'), $json, (New-Object Text.UTF8Encoding($false)))

$bin = '"' + (Join-Path $InstallDir $Exe) + '" --environment Production --contentRoot "' + $InstallDir + '"'
if ($existing) {
    & sc.exe config $ServiceName binPath= $bin start= auto | Out-Null
} else {
    New-Service -Name $ServiceName -BinaryPathName $bin -DisplayName 'DoveLoFanno' -Description 'Guida TV, registratore e libreria di casa' -StartupType Automatic | Out-Null
}
# riavvio dopo 10 s alle prime tre uscite anomale, contatore azzerato dopo un giorno
& sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/10000/restart/60000 | Out-Null

if (-not $NoFirewall) {
    $rule = Get-NetFirewallRule -DisplayName 'DoveLoFanno' -ErrorAction SilentlyContinue
    if ($rule) { $rule | Remove-NetFirewallRule }
    New-NetFirewallRule -DisplayName 'DoveLoFanno' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Private | Out-Null
}

Write-Host "Avvio del servizio"
Start-Service -Name $ServiceName
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    try { $h = Invoke-RestMethod "http://127.0.0.1:$Port/api/v1/health" -TimeoutSec 3; if ($h.status -eq 'ok') { $ok = $true; break } } catch { }
    Start-Sleep -Seconds 1
}
if (-not $ok) { throw "il servizio non risponde su http://127.0.0.1:$Port (vedere il Visualizzatore eventi, registro Applicazione)" }
Write-Host "DoveLoFanno in funzione: http://localhost:$Port  (dati: $DataDir, registrazioni: $RecordingsDir)"
if ($Log) { Stop-Transcript | Out-Null }
