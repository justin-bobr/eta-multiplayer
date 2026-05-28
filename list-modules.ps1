# =============================================================================
#  Listet alle Godot-Engine-Module aus Source\godot\modules\ und markiert
#  welche aktuell von build-engine.cmd deaktiviert werden.
#
#  Output:
#    [ X ] = aktiv (default in scons-build)
#    [DIS] = explicit disabled in build-engine.cmd
#    [???] = kritisch (mono, gdscript, etc) - NICHT deaktivieren
#
#  Damit kannst du sehen welche Module noch raus koennten + welche du
#  unbedingt behalten musst.
# =============================================================================
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

# Critical modules - DO NOT disable, Game braucht sie.
$critical = @(
    'mono'                    # C# support - keta.dll braucht das
    'gdscript'                # Auch wenn wir kein GDScript schreiben, Godot-internals nutzen es
    'text_server_advanced'    # Font rendering, HarfBuzz
    'text_server_fb'          # Fallback text server
    'freetype'                # Font loading
    'regex'                   # PCRE2 - intern von vielen things benutzt
    'glslang'                 # Shader compiler - braucht jeder Render-Pass
    'svg'                     # icon rendering im UI
    'minimp3'                 # Audio dekodierung
    'vorbis'                  # ogg audio
    'opus'                    # network audio (falls jemals voice chat)
    'theora'                  # video playback - eventuell entfernbar wenn keine cutscenes
    'jpg'                     # texture loading
    'webp'                    # texture loading
    'lightmapper_rd'          # baked lightmaps (de_dust2 nutzt das!)
    'raycast'                 # embree raycast - vermutlich nicht nur Jolt fuer Tools
)

# Currently in build-engine.cmd disable list - parse to stay in sync.
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
