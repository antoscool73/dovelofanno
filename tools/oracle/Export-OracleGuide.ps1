# Oracolo della wave 1: fa leggere una guida XMLTV al codice C# di RegistraTV (versione PowerShell)
# e scrive un TSV con quello che ne ricava, per confrontarlo con DoveLoFanno.Core.
# Gira con Windows PowerShell 5.1, come RegistraTV.
#
#   powershell -NoProfile -File Export-OracleGuide.ps1 -Guide <guida.xml.gz> [-Out <cartella>] [-RegistraTV <RegistraTV.ps1>]
#
# Senza -RegistraTV usa legacy\RegistraTV.EpgSource.cs (il sorgente al tag registratv-v1.0); con -RegistraTV
# prende la stringa $EpgSource da quello script. -Out predefinito: data\ accanto a questo script.
# Scrive nella cartella: la copia della guida (guide.xml.gz) e guide-oracle.tsv. La prima riga del TSV è
# "#now<TAB>yyyy-MM-ddTHH:mm:ss": l'istante usato per scartare i programmi finiti, da ripetere nel test.
param(
    [Parameter(Mandatory)] [string]$Guide,
    [string]$Out = (Join-Path $PSScriptRoot 'data'),
    [string]$RegistraTV
)
$ErrorActionPreference = 'Stop'

if ($RegistraTV) {
    # la stringa $EpgSource dello script, presa dall'AST senza eseguire lo script
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($RegistraTV, [ref]$null, [ref]$null)
    $assign = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq '$EpgSource' }, $true) | Select-Object -First 1
    if (-not $assign) { throw "EpgSource non trovato in $RegistraTV" }
    $source = $assign.Right.Expression.Value
} else {
    $source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'legacy\RegistraTV.EpgSource.cs'))
}

Add-Type -AssemblyName System.Xml.Linq
Add-Type -AssemblyName System.Web.Extensions
$refs = @('System.Xml', 'System.Xml.Linq', 'System.Web.Extensions', 'System.Core')
Add-Type -TypeDefinition $source -ReferencedAssemblies $refs -IgnoreWarnings

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$copy = Join-Path $Out 'guide.xml.gz'
Copy-Item -LiteralPath $Guide -Destination $copy -Force

# url vuoto: la guida viene letta dalla copia senza scaricare nulla
$now = Get-Date
$g = New-Object RegistraTV.EpgGuide('', $copy, [double]1e9)
$g.Start()
while (-not $g.Done) { Start-Sleep -Milliseconds 50 }
if ($g.Error) { throw "lettura della guida: $($g.Error)" }

$byId = $g.GetType().GetField('byId', [System.Reflection.BindingFlags]'NonPublic,Instance').GetValue($g)
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$clean = { param($s) ($s -replace "[`t`r`n]", ' ') }
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("#now`t" + $now.ToString('yyyy-MM-ddTHH:mm:ss', $inv))
foreach ($id in ($byId.Keys | Sort-Object)) {
    foreach ($p in $byId[$id]) {
        $lines.Add((@(
            $p.Channel,
            $p.Start.ToString('yyyy-MM-ddTHH:mm:ss', $inv),
            $p.Stop.ToString('yyyy-MM-ddTHH:mm:ss', $inv),
            (& $clean $p.Title), (& $clean $p.SubTitle), $p.Group, (& $clean $p.Sub), $p.GroupGuessed,
            $p.Year, (& $clean $p.Episode), (& $clean $p.WorkKey), $p.Desc.Length
        ) -join "`t"))
    }
}
[System.IO.File]::WriteAllLines((Join-Path $Out 'guide-oracle.tsv'), $lines, (New-Object System.Text.UTF8Encoding($false)))
"canali: $($g.ChannelCount), programmi: $($g.ProgrammeCount)"
