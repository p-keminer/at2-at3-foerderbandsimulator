param(
    [ValidateSet('Projekt','Ultraschall','Radar','Naeherung')]
    [string]$Station = 'Projekt',
    [switch]$Pruefen
)
$ErrorActionPreference = 'Stop'
$files = @{
    Projekt = 'AT1B_Pruefstaende.lvproj'
    Ultraschall = 'AT1B_Ultraschall.vi'
    Radar = 'AT1B_Radar.vi'
    Naeherung = 'AT1B_Naeherung.vi'
}
$required = @('AT1B_Pruefstaende.lvproj','AT1B_Ultraschall.vi','AT1B_Radar.vi','AT1B_Naeherung.vi',
    'Modelle\Trajektorie.vi','Modelle\AU004_Schritt.vi','Modelle\Radar_Schritt.vi',
    'Modelle\Naeherung_Schritt.vi','SubVIs\CSV_Speichern.vi','SubVIs\Aufzeichnung_Schritt.vi')
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $relative) -PathType Leaf)) {
        throw "Pruefstand unvollstaendig: $relative fehlt. Den gesamten Ordner kopieren."
    }
}
$target = Join-Path $PSScriptRoot $files[$Station]
if ($Pruefen) {
    Write-Output "Dateipruefung erfolgreich: $($required.Count) Dateien; Ziel: $target"
    return
}
# The user starts this script to open an interactive LabVIEW window.
# No MCP server, network listener, Siemens application or additional service is started.
Invoke-Item -LiteralPath $target
