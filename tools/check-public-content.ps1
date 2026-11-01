$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $root
try {
    # Inspect Git's actual publication candidates, including forcibly added ignored files.
    $files = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Kein prüfbarer Git-Arbeitsstand.' }
    $violations = [Collections.Generic.List[string]]::new()
    foreach ($relative in $files) {
        if ($relative -match '(?i)(^|/)(references|artifacts|output|tmp|text|logs|\.tools|\.packages|bin|obj)(/|$)' -or
            $relative -match '(?i)\.(dll|exe|msi|pdf|zip|7z|pfx|p12|pem|key|ap18|zap18)$' -or
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
    Write-Output 'Die Musterprüfung ersetzt keine inhaltliche Prüfung neuer Beiträge.'
} finally { Pop-Location }
