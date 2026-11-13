[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskManifest = Get-Content -LiteralPath (Join-Path $taskRoot 'extensions\manifest.json') -Raw | ConvertFrom-Json
$taskExpected = @(
    'extensions/AT1B/Pruefstand/AT1B_Ultraschall.vi',
    'extensions/AT1B/Pruefstand/AT1B_Radar.vi',
    'extensions/AT1B/Pruefstand/AT1B_Naeherung.vi',
    'extensions/AT1B/Pruefstand/Modelle/Trajektorie.vi',
    'extensions/AT1B/Pruefstand/Modelle/AU004_Schritt.vi',
    'extensions/AT1B/Pruefstand/Modelle/Radar_Schritt.vi',
    'extensions/AT1B/Pruefstand/Modelle/Naeherung_Schritt.vi',
    'extensions/AT1B/Pruefstand/SubVIs/CSV_Speichern.vi',
    'extensions/AT1B/Pruefstand/SubVIs/Aufzeichnung_Schritt.vi',
    'extensions/AT1B/PACTware/AT1B_Radar_Offline.PW6'
)
if (@($taskManifest.files).Count -ne $taskExpected.Count -or
    @(Compare-Object ($taskManifest.files.file | Sort-Object) ($taskExpected | Sort-Object)).Count) {
    throw 'Das Erweiterungsmanifest muss genau die neun VIs und das Offline-PW6-Projekt enthalten.'
}
foreach ($taskFile in $taskManifest.files) {
    $taskPath = Join-Path $taskRoot $taskFile.file
    if (!(Test-Path -LiteralPath $taskPath -PathType Leaf) -or
        (Get-Item -LiteralPath $taskPath).Length -ne $taskFile.bytes -or
        (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash -ne $taskFile.sha256) {
        throw "Datei fehlt oder wurde ohne Manifestaktualisierung geändert: $($taskFile.file)"
    }
    $taskBytes = [IO.File]::ReadAllBytes($taskPath)
    $taskSignature = [BitConverter]::ToString($taskBytes, 0, 8)
    if ($taskFile.file.EndsWith('.vi') -and $taskSignature -ne '52-53-52-43-0D-0A-00-03') {
        throw "Kein natives LabVIEW-VI: $($taskFile.file)"
    }
    if ($taskFile.file.EndsWith('.PW6') -and [Text.Encoding]::ASCII.GetString($taskBytes, 0, 16) -ne "SQLite format 3`0") {
        throw 'Unerwartetes PACTware-Dateiformat.'
    }
}
$taskPanelRoot = Join-Path $taskRoot 'extensions\AT1B\Pruefstand'
[xml]$taskProject = Get-Content -LiteralPath (Join-Path $taskPanelRoot 'AT1B_Pruefstaende.lvproj') -Raw
$taskReferences = @($taskProject.SelectNodes('//Item[@Type="VI"]'))
if ($taskReferences.Count -ne 9) { throw 'Unerwartete VI-Projektreferenzen.' }
foreach ($taskReference in $taskReferences) {
    $taskRelative = [string]$taskReference.URL
    if ([IO.Path]::IsPathRooted($taskRelative) -or $taskRelative -match '(^|[\\/])\.\.([\\/]|$)' -or
        !(Test-Path -LiteralPath (Join-Path $taskPanelRoot $taskRelative) -PathType Leaf)) {
        throw "Nicht portable VI-Referenz: $taskRelative"
    }
}
$taskSheets = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'extensions\AT1B\Messprotokolle') -Filter '*.csv' -File)
if ($taskSheets.Count -ne 8) { throw 'Es werden acht leere Messprotokolle erwartet.' }
foreach ($taskSheet in $taskSheets) {
    foreach ($taskRow in (Import-Csv -LiteralPath $taskSheet.FullName -Delimiter ';')) {
        foreach ($taskProperty in $taskRow.PSObject.Properties) {
            if ($taskProperty.Name -notin @('Sensor','Material','Probenbreite_mm','Normalabstand_s_mm') -and $taskProperty.Value -ne '') {
                throw "Vorausgefülltes Messergebnis: $($taskSheet.Name) / $($taskProperty.Name)"
            }
        }
    }
}
$taskRepoPrefix = [IO.Path]::GetFullPath($taskRoot) + [IO.Path]::DirectorySeparatorChar
foreach ($taskDocument in @('README.md','ANLEITUNG.md','THIRD_PARTY_LICENSES.md')) {
    $taskContent = [IO.File]::ReadAllText((Join-Path $taskRoot $taskDocument))
    foreach ($taskMatch in [regex]::Matches($taskContent, '\[[^\]]*\]\(([^)]+)\)')) {
        $taskLink = $taskMatch.Groups[1].Value
        if ($taskLink -match '^(https?:|mailto:)') { continue }
        $taskParts = $taskLink.Split('#', 2)
        $taskFile = if ($taskParts[0]) { [uri]::UnescapeDataString($taskParts[0].Trim('<','>')) } else { $taskDocument }
        $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskRoot $taskFile))
        if (!$taskTarget.StartsWith($taskRepoPrefix, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $taskTarget -PathType Leaf)) {
            throw "Fehlendes oder externes lokales Linkziel: $taskDocument / $taskLink"
        }
        if ($taskParts.Count -eq 2 -and $taskParts[1]) {
            $taskAnchor = [uri]::UnescapeDataString($taskParts[1])
            if (![IO.File]::ReadAllText($taskTarget).Contains('<a name="' + $taskAnchor + '"></a>')) {
                throw "Fehlender benannter Sprunganker: $taskDocument / $taskLink"
            }
        }
    }
}
Write-Output 'Erweiterungen geprüft: zehn native Dateien mit Prüfsummen, neun relative VI-Referenzen, acht leere Messprotokolle und lokale Dokumentationslinks. Keine Herstellerlaufzeit gestartet.'
