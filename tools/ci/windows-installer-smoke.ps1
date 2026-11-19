# Collaudo dell'installatore (Inno Setup) senza finestre: installazione da zero con il runtime di Microsoft scaricato
# davvero, dati via API, aggiornamento (installatore rilanciato) senza perdita di dati, disinstallazione che lascia i
# dati. Gira come amministratore in CI, dopo windows-service-smoke.ps1 (che lascia il PC senza servizio).
param([Parameter(Mandatory)] [string]$Setup)
$ErrorActionPreference = 'Stop'
$api = 'http://127.0.0.1:8765/api/v1'
$data = Join-Path $env:ProgramData 'DoveLoFanno'
$app = Join-Path $env:ProgramFiles 'DoveLoFanno'
$rec = Join-Path $env:RUNNER_TEMP 'Registrazioni prova'
$log = Join-Path $data 'logs\installazione.log'

function Step([string]$s) { Write-Host ''; Write-Host "== $s" }
function Check([bool]$ok, [string]$what) { if (-not $ok) { throw "collaudo non superato: $what" } }
function Run-Setup {
    $p = Start-Process -FilePath $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/RECORDINGS=`"$rec`"", "/LOG=`"$env:RUNNER_TEMP\setup.log`"" -Wait -PassThru
    Check ($p.ExitCode -eq 0) "installatore uscito con $($p.ExitCode)"
}

try {
    # si parte senza dati: il collaudo degli script prima di questo lascia il suo database
    if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }

    Step 'Runtime di Microsoft: download, controllo della firma, installazione'
    $prep = Join-Path $PSScriptRoot '..\..\deploy\windows\Prepare-DoveLoFanno.ps1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $prep -Step runtime -ForceDownload
    Check ($LASTEXITCODE -eq 0) 'runtime scaricato e installato'

    Step 'Installazione da zero'
    Run-Setup
    $svc = Get-CimInstance Win32_Service -Filter "Name='DoveLoFanno'"
    Check ($svc.State -eq 'Running') 'servizio in funzione'
    Check ($svc.StartMode -eq 'Auto') 'avvio automatico'
    Check ($svc.PathName -like "*$app\server\DoveLoFanno.Server.exe*") "percorso del servizio ($($svc.PathName))"
    Check (-not (Test-Path (Join-Path $app 'server\coreclr.dll'))) 'programma senza runtime incluso'
    Check ((Invoke-RestMethod "$api/health").api -eq 'v1') 'API v1'
    $cfg = Get-Content (Join-Path $app 'server\appsettings.Production.json') -Raw | ConvertFrom-Json
    Check ($cfg.DoveLoFanno.RecordingsDir -eq $rec) "cartella delle registrazioni ($($cfg.DoveLoFanno.RecordingsDir))"
    Check (Test-Path -LiteralPath $rec) 'cartella delle registrazioni creata'
    Check (Test-Path (Join-Path $app 'unins000.exe')) 'disinstallatore'
    Check (Test-Path -LiteralPath $log) 'registro dell''installazione'

    Step 'Dati attraverso l''API'
    $m3u = Join-Path $data 'prova.m3u'
    Set-Content -LiteralPath $m3u -Encoding UTF8 -Value "#EXTM3U`r`n#EXTINF:-1 tvg-id=`"Prova.it`",Canale di prova`r`nhttp://127.0.0.1:9/live.m3u8"
    $src = Invoke-RestMethod "$api/sources" -Method Post -ContentType 'application/json' -Body (@{ name = 'Prova'; url = $m3u } | ConvertTo-Json)
    Check ($src.channels -eq 1) 'playlist importata'

    Step 'Aggiornamento (installatore rilanciato)'
    Run-Setup
    Check ((Get-Service DoveLoFanno).Status -eq 'Running') 'servizio ripartito'
    Check (@(Invoke-RestMethod "$api/sources").Count -eq 1) 'playlist conservata'

    Step 'Disinstallazione (i dati restano)'
    $p = Start-Process -FilePath (Join-Path $app 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    # il disinstallatore si copia in una cartella temporanea e riparte da lì: si aspetta che abbia finito
    for ($i = 0; $i -lt 60 -and (Test-Path (Join-Path $app 'unins000.exe')); $i++) { Start-Sleep -Seconds 1 }
    Check ($null -eq (Get-Service DoveLoFanno -ErrorAction SilentlyContinue)) 'servizio tolto'
    Check (-not (Test-Path (Join-Path $app 'server'))) 'programma tolto'
    Check ($null -eq (Get-NetFirewallRule -DisplayName 'DoveLoFanno' -ErrorAction SilentlyContinue)) 'regola del firewall tolta'
    Check (Test-Path (Join-Path $data 'dlf.db')) 'database conservato'
    Write-Host ''
    Write-Host 'Collaudo dell''installatore riuscito'
}
catch {
    Write-Host "ERRORE: $($_.Exception.Message)"
    foreach ($f in $log, "$env:RUNNER_TEMP\setup.log") { if (Test-Path $f) { Write-Host "--- $f"; Get-Content $f -Tail 80 } }
    Get-WinEvent -LogName Application -MaxEvents 30 -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -match 'DoveLoFanno|\.NET Runtime|Application Error' } |
        ForEach-Object { Write-Host "$($_.TimeCreated) $($_.ProviderName): $($_.Message)" }
    throw
}
