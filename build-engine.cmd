@echo off
REM ============================================================================
REM  Build custom Godot export templates with module-strip.
REM  NOT the editor - that is fetched separately via setup-editor.cmd.
REM
REM  Layout:
REM    D:\Godot\Source\godot\   Godot source tree, auto-cloned
REM    D:\Godot\Engine\         Stock editor binaries
REM    D:\Godot\Engine\Templates\   Custom-built templates land here
REM
REM  What this script does automatically:
REM    1. git clone the Godot source if missing
REM    2. winget install Python if missing
REM    3. pip install scons if missing
REM    4. winget install VS 2022 Build Tools if missing
REM    5. scons build with aggressive module strip for FPS shooter
REM    6. Copy resulting templates to Engine\Templates
REM
REM  Duration: 30-60 min initial. Re-build with cache: 5-10 min.
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
if not exist "%GODOT_SRC%\bin\GodotSharp" (
    echo.
    echo [mono] building managed assemblies...
    pushd "%GODOT_SRC%"
    python modules\mono\build_scripts\build_assemblies.py --godot-output-dir=bin
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
if exist "%GODOT_SRC%\bin\GodotSharp" (
    if exist "%TEMPLATES_DIR%\GodotSharp" rmdir /s /q "%TEMPLATES_DIR%\GodotSharp"
    xcopy /E /I /Y "%GODOT_SRC%\bin\GodotSharp" "%TEMPLATES_DIR%\GodotSharp" >nul
)

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
