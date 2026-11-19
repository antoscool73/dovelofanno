#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Prerequisiti dell'installatore di DoveLoFanno: un passo per volta, così l'installatore mostra cosa sta facendo.

.DESCRIPTION
    -Step runtime  ASP.NET Core Runtime 10 (x64) di Microsoft: se manca lo scarica da Microsoft (aka.ms/dotnet),
                   controlla che il file sia firmato da Microsoft e lo installa senza finestre.
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

function Test-AspNetRuntime {
    # il runtime condiviso sta in Programmi\dotnet\shared\Microsoft.AspNetCore.App\10.x.y
    $root = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.AspNetCore.App'
    if (-not (Test-Path -LiteralPath $root)) { return $false }
    return [bool](Get-ChildItem -LiteralPath $root -Directory | Where-Object { $_.Name -match '^10\.' })
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
            if ((Test-AspNetRuntime) -and -not $ForceDownload) { Write-Host 'Runtime ASP.NET Core 10 già installato'; break }
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $url = 'https://aka.ms/dotnet/10.0/aspnetcore-runtime-win-x64.exe'
            $exe = Join-Path $env:TEMP ('aspnetcore-runtime-10-' + [guid]::NewGuid().ToString('N') + '.exe')
            Write-Host "Scarico il runtime da Microsoft: $url"
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
            if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { throw "installazione del runtime non riuscita (codice $($p.ExitCode))" }
            if (-not (Test-AspNetRuntime)) { throw 'runtime installato ma non trovato in Programmi\dotnet' }
            Write-Host 'Runtime ASP.NET Core 10 installato'
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
