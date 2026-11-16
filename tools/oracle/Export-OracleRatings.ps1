# Oracolo dei voti: per ogni film di una guida fa a TMDB le ricerche che fa RegistraTV, con il suo codice C#
# (legacy\RegistraTV.EpgSource.cs), e registra le risposte (solo i campi usati dall'abbinamento) e il film che
# RegistraTV sceglie. Il test rifà l'abbinamento con DoveLoFanno.Core sulle stesse risposte, senza rete.
# Gira con Windows PowerShell 5.1 sul PC: serve una chiave TMDB, che non finisce nel file prodotto.
#
#   powershell -NoProfile -File Export-OracleRatings.ps1 -Guide <guida.xml.gz> -Out <file.json> [-TmdbKey <chiave>]
#
# Senza -TmdbKey la chiave si legge da %LOCALAPPDATA%\RegistraTV\settings.json (TmdbKey).
param(
    [Parameter(Mandatory)] [string]$Guide,
    [Parameter(Mandatory)] [string]$Out,
    [string]$TmdbKey
)
$ErrorActionPreference = 'Stop'
if (-not $TmdbKey) { $TmdbKey = (Get-Content "$env:LOCALAPPDATA\RegistraTV\settings.json" -Raw | ConvertFrom-Json).TmdbKey }
if (-not $TmdbKey) { throw 'chiave TMDB mancante' }

Add-Type -AssemblyName System.Xml.Linq
Add-Type -AssemblyName System.Web.Extensions
$source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'legacy\RegistraTV.EpgSource.cs'))
Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.Xml', 'System.Xml.Linq', 'System.Web.Extensions', 'System.Core') -IgnoreWarnings

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('dlf-oracle-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Copy-Item -LiteralPath $Guide -Destination (Join-Path $work 'guide.xml.gz')
    $g = New-Object RegistraTV.EpgGuide('', (Join-Path $work 'guide.xml.gz'), [double]1e9)
    $g.Start()
    while (-not $g.Done) { Start-Sleep -Milliseconds 50 }
    if ($g.Error) { throw "lettura della guida: $($g.Error)" }
    $byId = $g.GetType().GetField('byId', [System.Reflection.BindingFlags]'NonPublic,Instance').GetValue($g)

    # un film per chiave della cache (titolo normalizzato|anno), come RatingService.Request
    $films = [ordered]@{}
    foreach ($id in $byId.Keys) {
        foreach ($p in $byId[$id]) {
            if ($p.Group -ne 'Film') { continue }
            $k = [RegistraTV.EpgGuide]::TitleKey($p.Title) + '|' + $p.Year
            if ($k.StartsWith('|') -or $films.Contains($k)) { continue }
            $films[$k] = $p
        }
    }

    $rs = New-Object RegistraTV.RatingService($TmdbKey, (Join-Path $work 'ratings'))
    $flags = [System.Reflection.BindingFlags]'NonPublic,Instance'
    $search = $rs.GetType().GetMethod('SearchMovie', $flags)
    $resolve = $rs.GetType().GetMethod('Resolve', $flags)
    $strip = {
        param($res)
        $list = @()
        foreach ($m in $res) {
            $f = @{}
            foreach ($n in 'id', 'title', 'original_title', 'release_date', 'vote_average', 'vote_count') {
                $v = $null
                if ($m.TryGetValue($n, [ref]$v)) { $f[$n] = $v } else { $f[$n] = $null }
            }
            $list += [pscustomobject]$f
        }
        , $list
    }

    $cases = New-Object System.Collections.Generic.List[object]
    $i = 0
    foreach ($k in $films.Keys) {
        $p = $films[$k]
        $i++
        Write-Progress -Activity 'Ricerche TMDB' -Status "$i / $($films.Count): $($p.Title)" -PercentComplete (100 * $i / $films.Count)
        $q = ([regex]::Replace($p.Title, '\s*\((film|tv|\d{4})\)\s*$', '', 'IgnoreCase')).Trim()
        $withYear = & $strip ($search.Invoke($rs, @($q, $p.Year)))
        $withoutYear = @()
        if ($p.Year -gt 0) { $withoutYear = & $strip ($search.Invoke($rs, @($q, 0))) }
        $e = $resolve.Invoke($rs, @($p.Title, $p.Year))
        $t = $e.GetType()
        $cases.Add([pscustomobject]@{
            title       = $p.Title
            year        = $p.Year
            withYear    = $withYear
            withoutYear = $withoutYear
            found       = [bool]$t.GetField('Found').GetValue($e)
            tmdbId      = [int]$t.GetField('TmdbId').GetValue($e)
            imdbId      = [string]$t.GetField('ImdbId').GetValue($e)
        })
        Start-Sleep -Milliseconds 120
    }
    $doc = [pscustomobject]@{
        note     = 'Risposte TMDB (solo i campi usati) e scelta di RegistraTV, per il test dell''abbinamento. Generato da tools/oracle/Export-OracleRatings.ps1.'
        recorded = (Get-Date).ToString('yyyy-MM-dd')
        cases    = $cases
    }
    $json = $doc | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($Out, $json, (New-Object System.Text.UTF8Encoding($false)))
    "film: $($cases.Count), trovati: $(@($cases | Where-Object found).Count)"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
