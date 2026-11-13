param([switch]$IncludeDocumentationDrafts)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $root
try {
    & (Join-Path $PSScriptRoot 'check-extensions.ps1')
    # Inspect Git's actual publication candidates, including forcibly added ignored files.
    $files = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Kein prüfbarer Git-Arbeitsstand.' }
    if ($IncludeDocumentationDrafts) {
        $files = @($files + @('README.md','ANLEITUNG.md','LICENSE','THIRD_PARTY_LICENSES.md','docs/images/offline.png') | Sort-Object -Unique)
    }
    $violations = [Collections.Generic.List[string]]::new()
    $tiaManifest = Get-Content -LiteralPath (Join-Path $root 'tia\manifest.json') -Raw | ConvertFrom-Json
    $approvedProjects = @('AT2_Start_Uebung_Originalstand.zap18','AT3_Start_Uebung_Originalstand.zap18','AT2_AT3_Referenzprojekt.zip','AT4B_Start_Uebung_Nachbildung_3AN02.zap18','AT4B_Technikpruefung_20261113.zap18')
    $extensionManifest = Get-Content -LiteralPath (Join-Path $root 'extensions\manifest.json') -Raw | ConvertFrom-Json
    $documentation = @($files | Where-Object { $_ -match '(?i)\.md$' -or $_ -eq 'LICENSE' } | Sort-Object)
    if (@(Compare-Object $documentation @('ANLEITUNG.md','LICENSE','README.md','THIRD_PARTY_LICENSES.md')).Count) {
        throw 'Es sind ausschließlich README, Anleitung und die beiden bestehenden Lizenzdateien zur Dokumentation vorgesehen.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($relative in $files) {
        if ($extensionManifest.files.file -contains $relative) { continue } # Exact allowlist, native signatures and hashes checked above.
        if ($relative.StartsWith('tia/') -and $approvedProjects -contains $relative.Substring(4)) {
            $projectPath = Join-Path $root $relative
            $entry = @($tiaManifest.projects | Where-Object { $_.file -eq $relative.Substring(4) })
            if ($entry.Count -ne 1 -or !(Test-Path -LiteralPath $projectPath) -or
                (Get-FileHash -LiteralPath $projectPath -Algorithm SHA256).Hash -ne $entry[0].sha256) {
                $violations.Add("Projektprüfsumme stimmt nicht: $relative"); continue
            }
            $archive = [IO.Compression.ZipFile]::OpenRead($projectPath)
            try {
                if (!@($archive.Entries | Where-Object { $_.FullName -like '*.ap18' }).Count) { $violations.Add("Kein natives TIA-Projekt: $relative") }
                foreach ($member in $archive.Entries) {
                    if ($member.FullName -match '(^|[\\/])\.\.([\\/]|$)|^[\\/]|^[A-Za-z]:' -or
                        $member.FullName -match '(?i)\.(exe|dll|msi|pfx|p12|key|pem)$') { $violations.Add("Unerlaubter Archiveintrag: $relative / $($member.FullName)") }
                }
            } finally { $archive.Dispose() }
            continue
        }
        if ($relative -match '(?i)(^|/)(references|artifacts|output|tmp|text|logs|\.tools|\.packages|bin|obj)(/|$)' -or
            $relative -match '(?i)\.(dll|exe|msi|pdf|zip|7z|pfx|p12|pem|key|ap18|zap18|vi|pw6)$' -or
            $relative -match '(?i)(^|/)\.env($|\.)|\.local\.json$') {
            $violations.Add("Nicht zur Veröffentlichung vorgesehen: $relative"); continue
        }
        $path = Join-Path $root $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { $violations.Add("Indizierte Datei fehlt: $relative"); continue }
        $item = Get-Item -LiteralPath $path
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $violations.Add("Verknüpfung im Export: $relative"); continue }
        if ($item.Length -gt 2MB) { $violations.Add("Unerwartet große Datei: $relative"); continue }
        if ($relative -match '(?i)\.png$') {
            $bytes = [IO.File]::ReadAllBytes($path)
            if ($relative -notmatch '^docs/images/' -or $bytes.Length -lt 8 -or [BitConverter]::ToString($bytes,0,8) -ne '89-50-4E-47-0D-0A-1A-0A') {
                $violations.Add("Unerwartetes Bildformat oder Bildverzeichnis: $relative")
            }
            continue # Screenshots also require visual inspection before publication.
        }
        $content = [IO.File]::ReadAllText($path)
        if ($content -match '(?i)[A-Z]:[\\/]+Users[\\/]+[^\\/\s]+' -or $content -match '/(?:home|Users)/[^/\s]+/') {
            $violations.Add("Persönlicher absoluter Pfad: $relative")
        }
        if ($content -match '(?i)gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,}|glpat-[A-Za-z0-9_-]{15,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----') {
            $violations.Add("Mögliches Zugangstoken oder privater Schlüssel: $relative")
        }
    }
    if ($violations.Count) { throw ($violations -join [Environment]::NewLine) }
    Write-Output "$($files.Count) Veröffentlichungskandidaten geprüft. Keine gesperrten Dateien, persönlichen Standardpfade oder erkannten Zugangstoken."
    Write-Output 'Die Musterprüfung ersetzt keine inhaltliche Prüfung neuer Dateien.'
} finally { Pop-Location }
