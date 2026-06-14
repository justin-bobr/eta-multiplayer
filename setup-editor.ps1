$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = $PSScriptRoot
$engine = Join-Path $root 'Engine'
$editorExe = Join-Path $engine 'Editor.exe'

if (-not (Test-Path $engine)) { New-Item -ItemType Directory -Path $engine | Out-Null }

if (Test-Path $editorExe) {
    Write-Host "Editor.exe bereits vorhanden in $engine"
    Write-Host "Loeschen und Script erneut ausfuehren um neueste Version zu holen."
    exit 0
}

Write-Host ""
Write-Host "=== GODOT EDITOR DOWNLOAD ==="
Write-Host "Target: $editorExe"
Write-Host ""

$tmpZip = Join-Path $env:TEMP ("godot-mono-{0}.zip" -f ([guid]::NewGuid().ToString('N')))
$tmpDir = Join-Path $env:TEMP ("godot-mono-{0}" -f ([guid]::NewGuid().ToString('N')))

Write-Host "[1/4] GitHub API: latest godot-builds release..."
$rel = Invoke-RestMethod 'https://api.github.com/repos/godotengine/godot-builds/releases/latest' `
    -Headers @{ 'User-Agent' = 'eta-setup' }

$asset = $rel.assets | Where-Object { $_.name -match 'mono_win64\.zip$' } | Select-Object -First 1
if (-not $asset) {
    Write-Host ""
    Write-Host "[ERR] Kein mono_win64.zip im latest release gefunden."
    Write-Host "Verfuegbare Assets:"
    $rel.assets | ForEach-Object { Write-Host "  $($_.name)" }
    throw 'C#/Mono Build fehlt im release'
}
if ($asset.name -notmatch 'mono') {
    throw "sanity-check fail: Asset '$($asset.name)' enthaelt nicht 'mono' im Namen"
}

Write-Host "  Release: $($rel.tag_name)"
Write-Host "  Asset:   $($asset.name)  ($([math]::Round($asset.size/1MB)) MB)"

Write-Host "[2/4] Download..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tmpZip

Write-Host "[3/4] Extract..."
Expand-Archive -LiteralPath $tmpZip -DestinationPath $tmpDir -Force
$inner = Get-ChildItem $tmpDir -Directory | Select-Object -First 1
if (-not $inner) { $inner = Get-Item $tmpDir }

Write-Host "[4/4] Pack into Engine\ as Editor.exe + Editor_console.exe..."
$mainExe = Get-ChildItem $inner.FullName -Filter '*mono_win64.exe' |
    Where-Object { $_.Name -notmatch 'console' } | Select-Object -First 1
$consoleExe = Get-ChildItem $inner.FullName -Filter '*mono_win64_console.exe' | Select-Object -First 1
$sharpDir = Get-ChildItem $inner.FullName -Directory -Filter 'GodotSharp' | Select-Object -First 1

if ($mainExe) {
    Copy-Item $mainExe.FullName (Join-Path $engine 'Editor.exe') -Force
}
if ($consoleExe) {
    Copy-Item $consoleExe.FullName (Join-Path $engine 'Editor_console.exe') -Force
}
if ($sharpDir) {
    $dstSharp = Join-Path $engine 'GodotSharp'
    if (Test-Path $dstSharp) { Remove-Item $dstSharp -Recurse -Force }
    Copy-Item $sharpDir.FullName $dstSharp -Recurse -Force
}

Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== EDITOR INSTALLED ==="
Get-ChildItem $engine -Filter 'Editor*.exe' | ForEach-Object {
    Write-Host ("  {0} MB  {1}" -f [math]::Round($_.Length/1MB), $_.Name)
}

$tplAsset = $rel.assets | Where-Object { $_.name -match 'mono_export_templates\.tpz$' } | Select-Object -First 1
if (-not $tplAsset) {
    Write-Host ""
    Write-Host "[WARN] No mono_export_templates.tpz in release - skipping templates."
    Write-Host "       Install via Godot Editor: Project > Manage Export Templates."
    exit 0
}

$tag = $rel.tag_name
$versionSlug = ($tag -replace '-', '.') + '.mono'
$tplDir = Join-Path $env:APPDATA "Godot\export_templates\$versionSlug"

if (Test-Path (Join-Path $tplDir 'windows_release_x86_64.exe')) {
    Write-Host ""
    Write-Host "Export templates already installed at $tplDir"
    exit 0
}

Write-Host ""
Write-Host "=== EXPORT TEMPLATES DOWNLOAD ==="
Write-Host ("  Target: {0}" -f $tplDir)
Write-Host ("  Asset:  {0} ({1} MB)" -f $tplAsset.name, [math]::Round($tplAsset.size/1MB))

$tplTmpZip = Join-Path $env:TEMP ("godot-templates-{0}.zip" -f ([guid]::NewGuid().ToString('N')))
$tplTmpDir = Join-Path $env:TEMP ("godot-templates-{0}" -f ([guid]::NewGuid().ToString('N')))

Write-Host "[1/3] Download..."
Invoke-WebRequest -Uri $tplAsset.browser_download_url -OutFile $tplTmpZip

Write-Host "[2/3] Extract..."
$tplTmpZipRenamed = $tplTmpZip + '.zip'
Move-Item $tplTmpZip $tplTmpZipRenamed -Force
Expand-Archive -LiteralPath $tplTmpZipRenamed -DestinationPath $tplTmpDir -Force

$tplInner = Join-Path $tplTmpDir 'templates'
if (-not (Test-Path $tplInner)) {
    $first = Get-ChildItem $tplTmpDir -Directory | Select-Object -First 1
    if ($first) { $tplInner = $first.FullName }
}

Write-Host "[3/3] Install to Godot templates cache..."
if (-not (Test-Path $tplDir)) { New-Item -ItemType Directory -Path $tplDir -Force | Out-Null }
Copy-Item (Join-Path $tplInner '*') $tplDir -Recurse -Force

Remove-Item $tplTmpZipRenamed -Force -ErrorAction SilentlyContinue
Remove-Item $tplTmpDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== TEMPLATES INSTALLED ==="
Get-ChildItem $tplDir | Where-Object { $_.Name -match 'linux|windows' } | ForEach-Object {
    Write-Host ("  {0} MB  {1}" -f [math]::Round($_.Length/1MB), $_.Name)
}
