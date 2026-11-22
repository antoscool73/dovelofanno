#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Prerequisiti dell'installatore di DoveLoFanno: un passo per volta, così l'installatore mostra cosa sta facendo.

.DESCRIPTION
    -Step runtime  .NET Runtime 10 e ASP.NET Core Runtime 10 (x64) di Microsoft: quello che manca lo scarica da
                   Microsoft (aka.ms/dotnet), controlla che il file sia firmato da Microsoft e lo installa senza finestre.
                   Servono tutti e due: l'installatore .exe di ASP.NET Core non porta il runtime .NET di base
                   (Microsoft.NETCore.App) e senza quello il servizio non parte (PC di prova pulito, 21/11/2026).
                   Il runtime di base deve essere almeno della versione che chiede ASP.NET Core: se è più vecchio
                   lo aggiorna.
    -Step ffmpeg   ffmpeg: se non c'è, lo installa con winget (pacchetto Gyan.FFmpeg, per tutto il PC, così lo vede
                   anche il servizio). Non lo distribuiamo noi.
    -Step stop     ferma il servizio DoveLoFanno, se c'è (prima di sostituire i file in un aggiornamento).
    Esce con 0 se tutto è andato bene; altrimenti scrive il motivo e esce con 1. Gira con Windows PowerShell 5.1.
#>
param(
    [Parameter(Mandatory)] [ValidateSet('runtime', 'ffmpeg', 'stop')] [string]$Step,
    [string]$Log = '',
    # collaudi: scarica e installa il runtime anche se c'è già (prova il download e il controllo della firma)
    [switch]$ForceDownload
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'      # la barra di avanzamento di Invoke-WebRequest rallenta molto il download
if ($Log) { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null; Start-Transcript -LiteralPath $Log -Append | Out-Null }

function Get-SharedRuntimeVersion([string]$Framework) {
    # i runtime condivisi stanno in Programmi\dotnet\shared\<framework>\10.x.y: la versione 10 più alta, senza le
    # anteprime (10.0.0-rc.2...), che il programma non usa; $null se non ce n'è
    $root = Join-Path $env:ProgramFiles ('dotnet\shared\' + $Framework)
    if (-not (Test-Path -LiteralPath $root)) { return $null }
    $v = Get-ChildItem -LiteralPath $root -Directory | Where-Object { $_.Name -match '^10\.\d+\.\d+$' } |
        ForEach-Object { [version]$_.Name } | Sort-Object -Descending | Select-Object -First 1
    return $v
}

function Test-SharedRuntime([string]$Framework) { return $null -ne (Get-SharedRuntimeVersion $Framework) }

function Get-AspNetBaseRequirement {
    # ASP.NET Core 10.0.N vuole il runtime di base almeno 10.0.N (rollForward LatestPatch nel suo runtimeconfig):
    # con un runtime di base più vecchio il servizio non parte e Windows dice solo "Impossibile avviare il servizio"
    $asp = Get-SharedRuntimeVersion 'Microsoft.AspNetCore.App'
    if (-not $asp) { return $null }
    $cfg = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.AspNetCore.App\$asp\Microsoft.AspNetCore.App.runtimeconfig.json"
    if (Test-Path -LiteralPath $cfg) {
        $fw = (Get-Content -LiteralPath $cfg -Raw | ConvertFrom-Json).runtimeOptions.framework
        if ($fw -and $fw.version -match '^\d+\.\d+\.\d+$') { return [version]$fw.version }
    }
    return $asp
}

function Install-SharedRuntime([string]$Name, [string]$Framework, [string]$Package, [switch]$Update) {
    if ((Test-SharedRuntime $Framework) -and -not $ForceDownload -and -not $Update) { Write-Host "$Name 10 già installato ($(Get-SharedRuntimeVersion $Framework))"; return }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $url = "https://aka.ms/dotnet/10.0/$Package-win-x64.exe"
    $exe = Join-Path $env:TEMP ("$Package-10-" + [guid]::NewGuid().ToString('N') + '.exe')
    Write-Host "Scarico $Name da Microsoft: $url"
    Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing
    $sig = Get-AuthenticodeSignature -LiteralPath $exe
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        Remove-Item -LiteralPath $exe -Force
        throw "il file scaricato non è firmato da Microsoft ($($sig.Status)): installazione fermata"
    }
    Write-Host "Firma: $($sig.SignerCertificate.Subject)"
    $p = Start-Process -FilePath $exe -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
    Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue
    # 3010 = installato, serve un riavvio di Windows (il servizio parte lo stesso)
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { throw "installazione di $Name non riuscita (codice $($p.ExitCode))" }
    if (-not (Test-SharedRuntime $Framework)) { throw "$Name installato ma non trovato in Programmi\dotnet\shared\$Framework" }
    Write-Host "$Name 10 installato ($(Get-SharedRuntimeVersion $Framework))"
}

function Find-Ffmpeg {
    $cmd = Get-Command ffmpeg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Path }
    foreach ($p in (Join-Path $env:ProgramFiles 'WinGet\Links\ffmpeg.exe'), (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\ffmpeg.exe')) {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

try {
    switch ($Step) {
        'runtime' {
            # prima il runtime di base, poi ASP.NET Core che si appoggia a lui
            Install-SharedRuntime 'Runtime .NET' 'Microsoft.NETCore.App' 'dotnet-runtime'
            Install-SharedRuntime 'Runtime ASP.NET Core' 'Microsoft.AspNetCore.App' 'aspnetcore-runtime'
            # il runtime di base già presente (messo da un altro programma o a mano) può essere più vecchio di quello
            # che chiede ASP.NET Core, che scarichiamo sempre all'ultima versione (PC di prova, 22/11/2026)
            $need = Get-AspNetBaseRequirement
            $base = Get-SharedRuntimeVersion 'Microsoft.NETCore.App'
            if ($need -and $base -lt $need) {
                Write-Host "Runtime .NET $base più vecchio di quello che serve ad ASP.NET Core ($need): lo aggiorno"
                Install-SharedRuntime 'Runtime .NET' 'Microsoft.NETCore.App' 'dotnet-runtime' -Update
                $base = Get-SharedRuntimeVersion 'Microsoft.NETCore.App'
                if ($base -lt $need) { throw "il runtime .NET $base è ancora più vecchio di $need, che serve ad ASP.NET Core" }
            }
        }
        'ffmpeg' {
            $found = Find-Ffmpeg
            if ($found) { Write-Host "ffmpeg già presente: $found"; break }
            $winget = Get-Command winget -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $winget) { throw 'ffmpeg non trovato e winget non disponibile: installare «Programma di installazione app» dal Microsoft Store, oppure ffmpeg a mano, e rilanciare l''installazione' }
            Write-Host 'Installo ffmpeg con winget (Gyan.FFmpeg)'
            & $winget.Path install --id Gyan.FFmpeg --exact --scope machine --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
            $found = Find-Ffmpeg
            if (-not $found) { throw "winget non è riuscito a installare ffmpeg (codice $LASTEXITCODE)" }
            Write-Host "ffmpeg installato: $found"
        }
        'stop' {
            $svc = Get-Service -Name 'DoveLoFanno' -ErrorAction SilentlyContinue
            if ($svc -and $svc.Status -ne 'Stopped') {
                Write-Host 'Fermo il servizio DoveLoFanno'
                Stop-Service -Name 'DoveLoFanno' -Force
                $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
            }
        }
    }
    if ($Log) { Stop-Transcript | Out-Null }
    exit 0
}
catch {
    Write-Host "ERRORE: $($_.Exception.Message)"
    if ($Log) { Stop-Transcript | Out-Null }
    exit 1
}
