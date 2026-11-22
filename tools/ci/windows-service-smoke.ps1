# Collaudo del servizio Windows (wave 3): installazione da zero, dati via API, aggiornamento (reinstallazione
# sopra) senza perdita di dati, backup, disinstallazione che lascia i dati. Gira come amministratore in CI.
param([Parameter(Mandatory)] [string]$Package)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$deploy = Join-Path $here '..\..\deploy\windows'
$api = 'http://127.0.0.1:8765/api/v1'
$data = Join-Path $env:ProgramData 'DoveLoFanno'

function Step([string]$s) { Write-Host ''; Write-Host "== $s" }
function Check([bool]$ok, [string]$what) { if (-not $ok) { throw "collaudo non superato: $what" } }

try {
    Step 'Installazione da zero'
    & (Join-Path $deploy 'Install-DoveLoFanno.ps1') -Package $Package
    $svc = Get-CimInstance Win32_Service -Filter "Name='DoveLoFanno'"
    Check ($svc.StartMode -eq 'Auto') 'avvio automatico'
    Check ($svc.State -eq 'Running') 'servizio in funzione'
    Check ((Invoke-RestMethod "$api/health").api -eq 'v1') 'API v1'
    Check ($null -ne (Get-NetFirewallRule -DisplayName 'DoveLoFanno' -ErrorAction SilentlyContinue)) 'regola del firewall'

    Step 'Dati attraverso l''API'
    $m3u = Join-Path $data 'prova.m3u'
    Set-Content -LiteralPath $m3u -Encoding UTF8 -Value "#EXTM3U`r`n#EXTINF:-1 tvg-id=`"Prova.it`",Canale di prova`r`nhttp://127.0.0.1:9/live.m3u8"
    $src = Invoke-RestMethod "$api/sources" -Method Post -ContentType 'application/json' -Body (@{ name = 'Prova'; url = $m3u } | ConvertTo-Json)
    Check ($src.channels -eq 1) 'playlist importata'
    $ch = (Invoke-RestMethod "$api/channels")[0].id
    $start = (Get-Date).AddDays(1).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $stop = (Get-Date).AddDays(1).AddHours(1).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $rec = Invoke-RestMethod "$api/recordings" -Method Post -ContentType 'application/json' -Body (@{ channelId = $ch; title = 'Domani'; start = $start; stop = $stop } | ConvertTo-Json)

    Step 'Aggiornamento (reinstallazione sopra)'
    & (Join-Path $deploy 'Install-DoveLoFanno.ps1') -Package $Package
    Check (@(Invoke-RestMethod "$api/sources").Count -eq 1) 'playlist conservata'
    Check ((Invoke-RestMethod "$api/recordings/$($rec.id)").recording.state -eq 'scheduled') 'registrazione conservata'

    Step 'Backup'
    $b = Invoke-RestMethod "$api/backups" -Method Post
    Check (Test-Path (Join-Path $data "backup\$($b.name)")) 'file di backup'

    Step 'Servizio che non parte: la causa finisce nel registro'
    # come sul PC di prova del 22/11/2026: il programma chiede un runtime che non c'è e Windows dice solo
    # "Impossibile avviare il servizio"; il registro deve riportare il messaggio del programma
    $broken = Join-Path $env:RUNNER_TEMP 'pacchetto-rotto'
    if (Test-Path -LiteralPath $broken) { Remove-Item -LiteralPath $broken -Recurse -Force }
    Copy-Item -LiteralPath $Package -Destination $broken -Recurse
    $rc = Join-Path $broken 'DoveLoFanno.Server.runtimeconfig.json'
    (Get-Content -LiteralPath $rc -Raw) -replace '"version":\s*"10\.[^"]*"', '"version": "99.0.0"' | Set-Content -LiteralPath $rc -Encoding UTF8
    $brokenLog = Join-Path $env:RUNNER_TEMP 'installazione-rotta.log'
    $failed = $false
    try { & (Join-Path $deploy 'Install-DoveLoFanno.ps1') -Package $broken -Log $brokenLog } catch { $failed = $true; Write-Host "Errore atteso: $($_.Exception.Message)" }
    try { Stop-Transcript | Out-Null } catch { }
    Check $failed 'installazione fermata'
    $text = Get-Content -LiteralPath $brokenLog -Raw
    Check ($text -match 'Diagnosi') 'diagnosi nel registro'
    Check ($text -match '99\.0\.0') 'messaggio del programma (runtime 99.0.0 mancante) nel registro'

    Step 'Disinstallazione (i dati restano)'
    & (Join-Path $deploy 'Uninstall-DoveLoFanno.ps1')
    Check ($null -eq (Get-Service DoveLoFanno -ErrorAction SilentlyContinue)) 'servizio tolto'
    Check (-not (Test-Path (Join-Path $env:ProgramFiles 'DoveLoFanno'))) 'programma tolto'
    Check (Test-Path (Join-Path $data 'dlf.db')) 'database conservato'
    Write-Host ''
    Write-Host 'Collaudo del servizio Windows riuscito'
}
catch {
    Write-Host "ERRORE: $($_.Exception.Message)"
    Get-WinEvent -LogName Application -MaxEvents 30 -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -match 'DoveLoFanno|\.NET Runtime|Application Error' } |
        ForEach-Object { Write-Host "$($_.TimeCreated) $($_.ProviderName): $($_.Message)" }
    $logs = Join-Path $data 'logs'
    if (Test-Path $logs) { Get-ChildItem $logs | ForEach-Object { Write-Host "--- $($_.Name)"; Get-Content $_.FullName -Tail 40 } }
    throw
}
