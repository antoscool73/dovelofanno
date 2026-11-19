# Compila l'installatore Inno Setup di DoveLoFanno. Uso in CI (Windows):
#   .\tools\ci\build-installer.ps1 -Package <cartella del dotnet publish win-x64> -Version 0.1.0 -OutputDir out
param(
    [Parameter(Mandatory)] [string]$Package,
    [Parameter(Mandatory)] [string]$Version,
    [string]$OutputDir = 'out'
)
$ErrorActionPreference = 'Stop'
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) {
    choco install innosetup -y --no-progress | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'installazione di Inno Setup non riuscita' }
}
$iss = Join-Path $PSScriptRoot '..\..\deploy\windows\installer\DoveLoFanno.iss'
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$out = (Resolve-Path $OutputDir).Path
$pkg = (Resolve-Path $Package).Path
& $iscc "/DAppVersion=$Version" "/DPackageDir=$pkg" "/DOutputDir=$out" $iss | Out-Host
if ($LASTEXITCODE -ne 0) { throw "ISCC non riuscito (codice $LASTEXITCODE)" }
$exe = Join-Path $out "DoveLoFanno-$Version-setup.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "installatore non trovato: $exe" }
Write-Host ("Installatore: {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
