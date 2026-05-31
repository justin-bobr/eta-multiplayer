@echo off
REM ============================================================================
REM  Build custom Godot editor AND export templates from Source/godot/.
REM
REM  Layout:
REM    D:\Godot\Source\godot\          Godot source tree, auto-cloned
REM    D:\Godot\Source\patches\        Local engine patches applied before build
REM    D:\Godot\Engine\Editor.exe      Patched editor lands here (replaces stock)
REM    D:\Godot\Engine\Editor_console.exe
REM    D:\Godot\Engine\GodotSharp\     Patched .NET runtime assemblies
REM    D:\Godot\Engine\Templates\      Custom-built export templates
REM
REM  What this script does automatically:
REM    1. git clone the Godot source if missing
REM    2. winget install Python if missing
REM    3. pip install scons if missing
REM    4. winget install VS 2022 Build Tools if missing
REM    5. D3D12 SDK deps install
REM    6. Generate Mono glue + C# assemblies (uses stock Editor_console.exe
REM       on first run; subsequent runs use the just-built patched editor)
REM    7. scons build EDITOR (full, no module strip)
REM    8. Copy editor to Engine\Editor.exe + Editor_console.exe + GodotSharp\
REM    9. scons build TEMPLATE with aggressive module strip for FPS shooter
REM   10. Copy template to Engine\Templates
REM
REM  Local patches in Source/patches/*.patch are auto-applied to Source/godot
REM  before scons runs (re-running this script is idempotent: --reverse --check
REM  detects already-applied patches and skips them).
REM
REM  Duration: 60-90 min initial (editor + template). Re-build with cache: 5-15 min.
REM ============================================================================
setlocal
cd /d "%~dp0"

set "GODOT_SRC=%~dp0Source\godot"
set "ENGINE_DIR=%~dp0Engine"
set "TEMPLATES_DIR=%ENGINE_DIR%\Templates"

REM ----------------------------------------------------------------------------
REM Pre-flight 1: Godot source present? If not, auto-clone.
REM ----------------------------------------------------------------------------
if not exist "%GODOT_SRC%\SConstruct" (
    where git >nul 2>&1
    if errorlevel 1 (
        echo.
        echo [ERR] git not in PATH
        echo Install: https://git-scm.com/download/win
        exit /b 1
    )
    if not exist "%~dp0Source" mkdir "%~dp0Source"
    echo.
    echo [setup] Godot source missing - cloning via git, depth=1, branch 4.6.3-stable...
    git clone --depth 1 --branch 4.6.3-stable https://github.com/godotengine/godot.git "%GODOT_SRC%"
    if errorlevel 1 (
        echo [ERR] git clone failed
        exit /b 1
    )
    echo [setup] Source ready at %GODOT_SRC%
    echo.
)

REM ----------------------------------------------------------------------------
REM Apply local engine patches from Source/patches/.
REM
REM We fingerprint the patch set (SHA1 + filename for each .patch, sorted) and
REM store it in Source/.applied-patches.txt. On every run we recompute and
REM compare:
REM   match    -> tree already in the expected patched state; skip reset and
REM               apply. This preserves source mtimes so scons cached object
REM               files stay valid (incremental rebuilds stay fast).
REM   mismatch -> a patch was added, removed, or its content changed. Hard-reset
REM               Source/godot to HEAD, then apply the CURRENT set of patches
REM               fresh. Without the reset, a removed patch would leave its
REM               changes in the tree forever - the apply loop only iterates
REM               over patches that currently exist.
REM
REM `git clean -fd` removes untracked files but respects .gitignore, so the
REM scons build cache under bin/ and .sconsign.dblite survive the reset.
REM ----------------------------------------------------------------------------
set "PATCH_DIR=%~dp0Source\patches"
set "PATCH_STATE=%~dp0Source\.applied-patches.txt"
set "PATCH_STATE_NEW=%~dp0Source\.applied-patches.new"

powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $p=@(Get-ChildItem '%PATCH_DIR%\*.patch' -ErrorAction SilentlyContinue | Sort-Object Name); if($p.Count -gt 0){ $p | ForEach-Object { '{0} {1}' -f (Get-FileHash $_.FullName -Algorithm SHA1).Hash, $_.Name } | Set-Content -Path '%PATCH_STATE_NEW%' -Encoding ASCII } else { Set-Content -Path '%PATCH_STATE_NEW%' -Value '' -Encoding ASCII -NoNewline }"
if errorlevel 1 (
    echo [ERR] Failed to compute patch fingerprint via PowerShell
    exit /b 1
)

if not exist "%PATCH_STATE%" goto :patches_need_reapply
fc /b "%PATCH_STATE%" "%PATCH_STATE_NEW%" >nul 2>&1
if errorlevel 1 goto :patches_need_reapply

echo.
echo [patch] no patch changes since last build - skipping reset and apply
del "%PATCH_STATE_NEW%" >nul 2>&1
goto :patches_done

:patches_need_reapply
echo.
echo === Patches changed - hard-resetting Source/godot and re-applying ===
REM IMPORTANT: every git invocation here MUST use `git -C "%GODOT_SRC%"` so it
REM operates on the INNER godot repo only. The workspace at %~dp0 is itself a
REM git repo (git-in-git), and a bare `git reset --hard` here without -C/cwd
REM scoping could in theory escape to the outer project repo and wipe user
REM code. The -C form makes the target explicit and impossible to misread.
git -C "%GODOT_SRC%" reset --hard HEAD
if errorlevel 1 goto :patch_reset_failed
git -C "%GODOT_SRC%" clean -fd
if errorlevel 1 goto :patch_clean_failed

if exist "%PATCH_DIR%\*.patch" (
    for %%P in ("%PATCH_DIR%\*.patch") do (
        git -C "%GODOT_SRC%" apply --whitespace=nowarn "%%P"
        if errorlevel 1 (
            echo [ERR] git apply failed for %%~nxP
            echo The source tree may have drifted from what the patch expects.
            echo Inspect the patch and reconcile manually.
            exit /b 1
        )
        echo [patch] applied %%~nxP
    )
) else (
    echo [patch] no patches present - tree is now vanilla
)

move /y "%PATCH_STATE_NEW%" "%PATCH_STATE%" >nul
goto :patches_done

:patch_reset_failed
echo.
echo [ERR] git reset --hard HEAD failed in %GODOT_SRC%
exit /b 1
:patch_clean_failed
echo.
echo [ERR] git clean -fd failed in %GODOT_SRC%
exit /b 1

:patches_done
echo.

REM ----------------------------------------------------------------------------
REM Pre-flight 2: winget available? Needed for auto-install.
REM ----------------------------------------------------------------------------
where winget >nul 2>&1
if errorlevel 1 (
    echo.
    echo [ERR] winget not available - requires Windows 10 1809+ or Windows 11
    echo Install Python 3.10+, scons, and VS 2022 with C++ Build Tools manually.
    exit /b 1
)

REM ----------------------------------------------------------------------------
REM Pre-flight 3: Python present? Auto-install via winget.
REM ----------------------------------------------------------------------------
where python >nul 2>&1
if errorlevel 1 (
    echo.
    echo [setup] Python not installed - winget install Python.Python.3.12 ...
    winget install -e --id Python.Python.3.12 --silent --accept-source-agreements --accept-package-agreements
    echo.
    echo Python installed. Close cmd and open a new one,
    echo then run build-engine.cmd again. PATH update needs a fresh shell.
    exit /b 0
)

REM ----------------------------------------------------------------------------
REM Pre-flight 4: scons available as a Python module?
REM We invoke it as `python -m SCons` later, which sidesteps the user-scripts
REM PATH issue entirely (pip --user puts scons.exe in %APPDATA%\Python\... but
REM the Python interpreter itself is on PATH and finds the module fine).
REM ----------------------------------------------------------------------------
python -c "import SCons" >nul 2>&1
if errorlevel 1 (
    echo.
    echo [setup] scons not installed - installing via pip...
    python -m pip install scons
    if errorlevel 1 (
        echo [ERR] pip install scons failed
        exit /b 1
    )
    python -c "import SCons" >nul 2>&1
    if errorlevel 1 (
        echo [ERR] scons installed but Python cannot import it
        exit /b 1
    )
)

REM ----------------------------------------------------------------------------
REM Pre-flight 5: VS 2022 C++ Build Tools.
REM Registry check avoids the wildcard-parse issue of vswhere - VS writes
REM SharedInstallationPath under HKLM\SOFTWARE\Microsoft\VisualStudio\Setup.
REM ----------------------------------------------------------------------------
reg query "HKLM\SOFTWARE\Microsoft\VisualStudio\Setup" /v SharedInstallationPath >nul 2>&1
if errorlevel 1 (
    echo.
    echo [setup] VS 2022 Build Tools not installed - winget install, ~6 GB ...
    winget install -e --id Microsoft.VisualStudio.2022.BuildTools --silent --accept-source-agreements --accept-package-agreements --override "--wait --quiet --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
    if errorlevel 1 (
        echo [ERR] winget install VS Build Tools failed
        exit /b 1
    )
    echo.
    echo VS 2022 Build Tools installed. Close cmd and open a new one,
    echo then run build-engine.cmd again.
    exit /b 0
)

REM ----------------------------------------------------------------------------
REM Pre-flight 6: D3D12 SDK deps. Godot's D3D12 backend pulls Microsoft Agility
REM SDK + Mesa NIR + dxc shader compiler. The Godot install script puts them in
REM %LOCALAPPDATA%\Godot\build_deps - presence of agility_sdk\ there means done.
REM ----------------------------------------------------------------------------
if not exist "%LOCALAPPDATA%\Godot\build_deps\agility_sdk" (
    echo.
    echo [setup] Installing D3D12 SDK dependencies...
    pushd "%GODOT_SRC%"
    python misc\scripts\install_d3d12_sdk_windows.py
    popd
)
if not exist "%LOCALAPPDATA%\Godot\build_deps\agility_sdk" (
    echo [ERR] D3D12 SDK install did not produce the expected files.
    echo Alternative: edit build-engine.cmd, add 'd3d12=no' to the scons args.
    exit /b 1
)

if not exist "%TEMPLATES_DIR%" mkdir "%TEMPLATES_DIR%"

REM ----------------------------------------------------------------------------
REM Pre-flight 7: Mono glue + managed assemblies. The template_release build
REM needs both to be present in the source tree BEFORE scons can build a mono-
REM enabled template. We use the stock Editor.exe (which is itself a mono build)
REM to generate the glue files - this avoids having to compile our own editor.
REM ----------------------------------------------------------------------------
set "EDITOR_EXE=%~dp0Engine\Editor_console.exe"
if not exist "%EDITOR_EXE%" (
    echo.
    echo [ERR] Editor binary missing at %EDITOR_EXE%
    echo Run setup-editor.cmd first.
    exit /b 1
)
if not exist "%GODOT_SRC%\modules\mono\glue\GodotSharp\GodotSharp\Generated" (
    echo.
    echo [mono] generating mono glue via stock Editor...
    "%EDITOR_EXE%" --headless --generate-mono-glue "%GODOT_SRC%\modules\mono\glue"
    if errorlevel 1 (
        echo [ERR] mono glue generation failed
        exit /b 1
    )
)
REM Check on Api/ specifically, not the GodotSharp/ parent: a previous build that
REM failed half-way through (e.g. MSBuild error for GodotSharpEditor) leaves Tools/
REM in place while Api/ is missing, and the old `if not exist bin\GodotSharp` check
REM saw the orphaned Tools/ and skipped build_assemblies entirely — re-runs were
REM stuck failing forever. Checking the actual output that the next step needs
REM (Api/) auto-recovers from those half-built states.
if not exist "%GODOT_SRC%\bin\GodotSharp\Api" (
    echo.
    echo [mono] building managed assemblies...
    REM Wipe any stale partial output so build_assemblies starts from a clean slate.
    if exist "%GODOT_SRC%\bin\GodotSharp" rmdir /s /q "%GODOT_SRC%\bin\GodotSharp"

    REM Make sure the local nupkg feed directory exists BEFORE build_assemblies pushes
    REM into it. Godot.NET.Sdk's MSBuild target only creates intermediate dirs on its
    REM build output path, not the push target.
    if not exist "%ENGINE_DIR%\GodotSharp\Tools\nupkgs" mkdir "%ENGINE_DIR%\GodotSharp\Tools\nupkgs"

    REM --push-nupkgs-local kicks in two MSBuild properties inside build_assemblies.py:
    REM   /p:ClearNuGetLocalCache=true   -> auto-purges ~/.nuget/packages/godot*/<version>
    REM   /p:PushNuGetToLocalSource=...  -> copies the freshly-built .nupkg files to <path>
    REM This is the OFFICIAL workflow (docs.godotengine.org -> compiling_with_dotnet).
    REM Without this, NuGet keeps a vanilla GodotSharp 4.6.3 from nuget.org in the user
    REM cache and the project's PackageReference resolves to that, so any C# type added
    REM by an engine patch (e.g. PhysicsRayQueryResult3D) is missing at compile time
    REM with a CS0246 "type not found" - even though Engine\GodotSharp\Api\...\.dll has it.
    REM Game\NuGet.config maps Godot* packages to the same path so the patched nupkgs win.
    pushd "%GODOT_SRC%"
    python modules\mono\build_scripts\build_assemblies.py --godot-output-dir=bin --push-nupkgs-local "%ENGINE_DIR%\GodotSharp\Tools\nupkgs"
    popd
)
REM Verify by output existence rather than capturing errorlevel inside the
REM parenthesised block (cmd resolves vars at block-entry, not at runtime).
REM The script can print "non-zero" but still produce correct output dirs.
if not exist "%GODOT_SRC%\bin\GodotSharp\Api" (
    echo [ERR] build_assemblies.py did not produce bin\GodotSharp\Api
    echo Re-run build-engine.cmd or check the python script output above.
    exit /b 1
)

REM ============================================================================
REM EDITOR BUILD - full editor with mono, no module strip.
REM
REM Why we build our own editor in addition to the template:
REM   * We carry local engine patches in Source/patches/ (e.g. the
REM     compositor-allocation fix) that the stock editor doesn't have.
REM   * Patches are auto-applied above before this build step runs.
REM
REM Module strip is NOT applied here - editor needs the full set (asset import,
REM gltf, csg, gridmap, etc.). Only the template build strips down.
REM ============================================================================
echo.
echo === GODOT EDITOR BUILD - full, mono, patched ===
pushd "%GODOT_SRC%"
python -m SCons platform=windows target=editor production=yes module_mono_enabled=yes
set "SCONS_EDITOR_RC=%errorlevel%"
popd

if not "%SCONS_EDITOR_RC%"=="0" (
    echo.
    echo [ERR] editor scons build failed with rc=%SCONS_EDITOR_RC%
    exit /b 1
)

echo.
echo [Copy] godot.windows.editor.x86_64.mono.exe -^> Engine\Editor.exe
REM Windows blocks overwriting a running executable. Previously this `copy /Y`
REM failed silently (>nul suppressed the "file in use" stderr message) so the
REM script claimed success while Editor.exe stayed at the OLD build and only
REM Editor_console.exe got refreshed - exactly the symptom Stefan reported.
REM We now check errorlevel after each copy and bail out with a clear
REM instruction. The earlier `tasklist` pre-check produced false positives
REM (`Editor.exe` is a common process name on Windows - VS, other tools, even
REM the OS itself can have processes that match), so the pre-check was removed.
REM We restructured with goto-labels because CMD's `if errorlevel` evaluation
REM inside nested parenthesised blocks is parse-time, not run-time, and would
REM not behave as expected.
copy /Y "%GODOT_SRC%\bin\godot.windows.editor.x86_64.mono.exe" "%ENGINE_DIR%\Editor.exe" >nul 2>&1
if errorlevel 1 goto :editor_copy_failed

if exist "%GODOT_SRC%\bin\godot.windows.editor.x86_64.mono.console.exe" (
    echo [Copy] godot.windows.editor.x86_64.mono.console.exe -^> Engine\Editor_console.exe
    copy /Y "%GODOT_SRC%\bin\godot.windows.editor.x86_64.mono.console.exe" "%ENGINE_DIR%\Editor_console.exe" >nul 2>&1
    if errorlevel 1 goto :editor_console_copy_failed
)
goto :editor_copy_ok

:editor_copy_failed
echo.
echo [ERR] copy of Editor.exe failed.
echo Source: %GODOT_SRC%\bin\godot.windows.editor.x86_64.mono.exe
echo Target: %ENGINE_DIR%\Editor.exe
echo Most common cause: the Godot Editor is currently running, which locks
echo the .exe. Close all Godot Editor windows and re-run build-engine.cmd.
echo Other causes: source file missing, target dir not writable.
exit /b 1

:editor_console_copy_failed
echo.
echo [ERR] copy of Editor_console.exe failed - the file is most likely in use.
echo Close Editor_console.exe and re-run build-engine.cmd.
exit /b 1

:editor_copy_ok
REM GodotSharp folder MUST sit next to the editor exe - contains the .NET runtime
REM hostfxr, the generated GodotSharp.dll, and the API XML docs. Without it the
REM editor cannot load any C# project.
REM
REM Same failure mode as the Editor.exe copy: if any Godot host process has the
REM managed runtime DLLs loaded, rmdir fails on the locked file and the
REM subsequent xcopy then only fills in the missing pieces around a
REM partially-old tree -> the editor picks up a mismatched mix of Api XML,
REM GodotSharp.dll, and native runtime libs. The flow uses flat GOTO-labels
REM because CMD's `if errorlevel` inside nested parenthesised blocks is
REM unreliable - same lesson we learned with the Editor.exe copy.
REM
REM Note: rmdir reports errorlevel for "file in use" but ALSO for "path not
REM found" (errorlevel 2). We handle the not-found case explicitly via
REM `if exist` so the only failure path is the in-use case.
if not exist "%GODOT_SRC%\bin\GodotSharp" goto :godotsharp_copy_ok
if not exist "%ENGINE_DIR%\GodotSharp" goto :godotsharp_xcopy
rmdir /s /q "%ENGINE_DIR%\GodotSharp"
if errorlevel 1 goto :godotsharp_remove_failed

:godotsharp_xcopy
xcopy /E /I /Y "%GODOT_SRC%\bin\GodotSharp" "%ENGINE_DIR%\GodotSharp" >nul
if errorlevel 1 goto :godotsharp_copy_failed
goto :godotsharp_copy_ok

:godotsharp_remove_failed
echo.
echo [ERR] Could not delete %ENGINE_DIR%\GodotSharp before re-copying.
echo This usually means a Godot host process still has the managed runtime DLLs
echo (GodotSharp.dll, hostfxr.dll, etc.) loaded. Close ALL Godot windows
echo (Editor.exe + Editor_console.exe + any running client/server build) and
echo re-run build-engine.cmd.
exit /b 1

:godotsharp_copy_failed
echo.
echo [ERR] xcopy of GodotSharp folder failed.
echo Source: %GODOT_SRC%\bin\GodotSharp
echo Target: %ENGINE_DIR%\GodotSharp
echo Most likely cause: a Godot host process has runtime DLLs loaded - close
echo it and re-run. Otherwise verify the source folder exists and the target
echo is writable.
exit /b 1

:godotsharp_copy_ok

echo.
echo === GODOT TEMPLATE BUILD - custom, stripped, mono ===
echo Source:    %GODOT_SRC%
echo Output:    %TEMPLATES_DIR%\windows_release.x86_64.exe
echo.

REM ----------------------------------------------------------------------------
REM Module disable list for an FPS shooter using LiteNetLib UDP + Jolt Physics.
REM
REM Disabled:
REM   bullet           - Jolt Physics replaces it
REM   navigation       - no NavMesh pathfinding
REM   webxr / openxr   - no VR
REM   mobile_vr        - no mobile VR
REM   csg              - editor-only constructive solid geometry
REM   gridmap          - 3D tile editor, not used at runtime
REM   camera           - webcam capture
REM   websocket        - LiteNetLib UDP instead
REM   webrtc           - no peer-to-peer
REM   enet             - LiteNetLib instead
REM   multiplayer      - Godot's high-level MP - we use NetServer/NetClient direct
REM   mbedtls          - no HTTPS request from game
REM   upnp             - no auto port forwarding
REM   basis_universal  - mobile texture format
REM   squish           - editor-only texture compression
REM   gltf             - assets are pre-imported to .scn in .godot/imported/
REM                      at editor time, so runtime template does not load .glb
REM                      files. Re-enable here if assets fail to load post-build.
REM
REM Kept:
REM   mono             - C# support, keta.dll requires it
REM   gdscript         - Godot internals
REM   text_server_*    - font and UI rendering
REM   freetype         - font loading
REM   glslang          - shader compiler
REM   lightmapper_rd   - de_dust2 uses baked lightmap GI
REM   jpg/webp         - texture loading
REM   vorbis/opus      - audio
REM   svg              - icon rendering
REM ----------------------------------------------------------------------------

REM Conservative disable set: only modules with no runtime dependency cycle.
REM Tried aggressive set with xatlas_unwrap/meshoptimizer/fbx/etc - some are
REM dependencies of lightmapper_rd which dust2 needs. Build error was:
REM   editor_settings.h C3668 'override' has no base method
REM   -> lightmapper_rd.cpp depends on xatlas_unwrap. Re-enabled it.
REM
REM Safe additions in this set:
REM   astcenc / etcpak / ktx  - pure mobile texture formats, no desktop usage
REM   navigation_2d/_3d        - we use no NavMesh pathfinding
REM
REM Risky modules NOT disabled (untested, build may break):
REM   xatlas_unwrap            - lightmapper_rd depends on it (verified)
REM   meshoptimizer            - editor mesh optim, may have runtime ties
REM   vhacd                    - editor convex decomposition
REM   fbx                      - assets are imported but FBX classes referenced?
REM   jsonrpc / objectdb_profiler / betsy / cvtt / interactive_music
REM     - test individually before adding to this list

pushd "%GODOT_SRC%"
python -m SCons platform=windows target=template_release production=yes ^
    module_mono_enabled=yes ^
    module_bullet_enabled=no ^
    module_navigation_2d_enabled=no ^
    module_navigation_3d_enabled=no ^
    module_webxr_enabled=no ^
    module_openxr_enabled=no ^
    module_mobile_vr_enabled=no ^
    module_csg_enabled=no ^
    module_gridmap_enabled=no ^
    module_camera_enabled=no ^
    module_websocket_enabled=no ^
    module_webrtc_enabled=no ^
    module_enet_enabled=no ^
    module_multiplayer_enabled=no ^
    module_mbedtls_enabled=no ^
    module_upnp_enabled=no ^
    module_basis_universal_enabled=no ^
    module_squish_enabled=no ^
    module_gltf_enabled=no ^
    module_astcenc_enabled=no ^
    module_etcpak_enabled=no ^
    module_ktx_enabled=no ^
    module_godot_physics_2d_enabled=no ^
    module_godot_physics_3d_enabled=no
set "SCONS_RC=%errorlevel%"
popd

REM godot_physics_2d/3d are EXPERIMENTAL disables - project.godot uses Jolt
REM for 3D so godot_physics_3d is dead code. godot_physics_2d is the only 2D
REM physics impl in Godot; we have no 2D physics in scenes, but if anything
REM references it the build will fail. If so, remove those two lines above.

if not "%SCONS_RC%"=="0" (
    echo.
    echo [ERR] scons build failed with rc=%SCONS_RC%
    echo If error mentions a missing module dependency,
    echo comment out the corresponding disable line in build-engine.cmd.
    exit /b 1
)

echo.
echo [Copy] template -^> Engine\Templates
REM Mono builds produce a .mono.exe variant; fall back to non-mono name if not present.
if exist "%GODOT_SRC%\bin\godot.windows.template_release.x86_64.mono.exe" (
    copy /Y "%GODOT_SRC%\bin\godot.windows.template_release.x86_64.mono.exe" "%TEMPLATES_DIR%\windows_release.x86_64.exe" >nul
) else (
    copy /Y "%GODOT_SRC%\bin\godot.windows.template_release.x86_64.exe" "%TEMPLATES_DIR%\windows_release.x86_64.exe" >nul
)
REM Also copy the GodotSharp folder which is the managed runtime that the template loads.
REM Flat goto-flow + errorlevel checks for the same reasons as the editor-side
REM copy above. Templates\GodotSharp is much less likely to be locked (templates
REM are not executed directly during editor work) but we keep the structure
REM consistent so future "silent failed copy" bugs get caught early.
if not exist "%GODOT_SRC%\bin\GodotSharp" goto :templates_godotsharp_ok
if not exist "%TEMPLATES_DIR%\GodotSharp" goto :templates_godotsharp_xcopy
rmdir /s /q "%TEMPLATES_DIR%\GodotSharp"
if errorlevel 1 goto :templates_godotsharp_remove_failed

:templates_godotsharp_xcopy
xcopy /E /I /Y "%GODOT_SRC%\bin\GodotSharp" "%TEMPLATES_DIR%\GodotSharp" >nul
if errorlevel 1 goto :templates_godotsharp_copy_failed
goto :templates_godotsharp_ok

:templates_godotsharp_remove_failed
echo.
echo [ERR] Could not delete %TEMPLATES_DIR%\GodotSharp before re-copying.
echo A process likely has the runtime DLLs loaded - close any running game
echo client/server that uses the export template and re-run.
exit /b 1

:templates_godotsharp_copy_failed
echo.
echo [ERR] xcopy of Templates\GodotSharp failed.
echo Source: %GODOT_SRC%\bin\GodotSharp
echo Target: %TEMPLATES_DIR%\GodotSharp
exit /b 1

:templates_godotsharp_ok

echo.
echo === BUILD OK ===
echo Template:
for %%F in ("%TEMPLATES_DIR%\windows_release.x86_64.exe") do echo   %%~zF bytes  %%~nxF
echo.
echo Next step: in Godot Editor -^> Project -^> Export -^> Windows preset
echo   -^> Advanced Settings ON
echo   -^> Custom Template Release: %TEMPLATES_DIR%\windows_release.x86_64.exe
echo Then run build-windows.cmd.
echo.
echo For Linux template: copy build-engine.sh + Source/godot to a Linux host
echo or WSL, then bash build-engine.sh - same module strip, native gcc build.
echo.
endlocal
