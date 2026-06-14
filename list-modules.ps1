$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$modulesDir = Join-Path $root 'Source\godot\modules'
$buildScript = Join-Path $root 'build-engine.cmd'

if (-not (Test-Path $modulesDir)) {
    Write-Host ""
    Write-Host "Source\godot\modules nicht gefunden."
    Write-Host "Erst build-engine.cmd ausfuehren (clont Source automatisch)."
    exit 1
}

$critical = @(
    'mono'
    'gdscript'
    'text_server_advanced'
    'text_server_fb'
    'freetype'
    'regex'
    'glslang'
    'svg'
    'minimp3'
    'vorbis'
    'opus'
    'theora'
    'jpg'
    'webp'
    'lightmapper_rd'
    'raycast'
)

$disabledNames = @()
if (Test-Path $buildScript) {
    $content = Get-Content $buildScript -Raw
    $matches = [regex]::Matches($content, 'module_(\w+)_enabled=no')
    $disabledNames = $matches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
}

$dirs = Get-ChildItem $modulesDir -Directory | Sort-Object Name

Write-Host ""
Write-Host "=== Godot Module ($($dirs.Count) total) ==="
Write-Host ""
Write-Host "  legende:  [ X ] aktiv   [DIS] in build-engine.cmd deaktiviert   [!!!] kritisch (nicht deaktivieren)"
Write-Host ""

$active = 0
$disabledCount = 0
$criticalCount = 0

foreach ($d in $dirs) {
    $name = $d.Name
    if ($name -eq 'register_module.h' -or $name.StartsWith('.')) { continue }

    $marker = '[ X ]'
    $note = ''
    if ($disabledNames -contains $name) {
        $marker = '[DIS]'
        $disabledCount++
    } elseif ($critical -contains $name) {
        $marker = '[!!!]'
        $note = '   <- keep'
        $criticalCount++
    } else {
        $active++
    }

    Write-Host ("  {0}  {1,-30}{2}" -f $marker, $name, $note)
}

Write-Host ""
Write-Host "=== Summary ==="
Write-Host ("  active:    {0}" -f $active)
Write-Host ("  disabled:  {0}  (siehe build-engine.cmd)" -f $disabledCount)
Write-Host ("  critical:  {0}  (nicht anfassen)" -f $criticalCount)
Write-Host ""
Write-Host "Disable-Kandidaten = aktive Module die NICHT critical sind."
Write-Host "Pruefe pro Modul ob dein Game's Features das nutzt (z.B. Particles, Audio, etc)."
Write-Host "Dann in build-engine.cmd ein 'module_<name>_enabled=no' hinzufuegen."
