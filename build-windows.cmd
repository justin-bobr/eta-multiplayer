@echo off
REM ============================================================================
REM  VANTIX Windows - Release Build.
REM  Single binary used as both client and server:
REM    Export\Windows\vantix.exe                              -> client
REM    Export\Windows\vantix.exe --headless -- --server       -> dedicated server
REM
REM  Output: D:\Godot\Export\Windows\vantix.exe
REM  Duration: ~3-5 min.
REM ============================================================================
setlocal
cd /d "%~dp0"

if defined GODOT if not exist "%GODOT%" set "GODOT="
if not defined GODOT set "GODOT=%~dp0Engine\Editor_console.exe"
if not exist "%GODOT%" (
    echo [WARN] %GODOT% not found - falling back to 'godot' on PATH
    set "GODOT=godot"
)

REM Which template Godot uses is set in Game\export_presets.cfg, not via this
REM script - we only log local template existence for awareness.
if exist "%~dp0Engine\Templates\windows_release.x86_64.exe" (
    echo [INFO] Custom stripped template available at Engine\Templates
) else (
    echo [INFO] No custom template - using Godot stock from AppData\Godot\export_templates
)

echo.
echo === VANTIX WINDOWS BUILD ===
echo Godot:    %GODOT%
echo Template: %TEMPLATE%
echo Project:  %~dp0Game
echo Output:   %~dp0Export\Windows\vantix.exe
echo.

echo [1/2] dotnet build, ExportRelease...
dotnet build "Game\vantix.csproj" -c ExportRelease -nologo
if errorlevel 1 (
    echo [ERR] dotnet build failed
    exit /b 1
)

echo.
echo [2/2] Godot export...
if not exist "Export\Windows" mkdir "Export\Windows"
"%GODOT%" --headless --path "Game" --export-release "Windows" "%~dp0Export\Windows\vantix.exe"
if errorlevel 1 (
    echo [ERR] Godot export failed
    exit /b 1
)

REM ----------------------------------------------------------------------------
REM Generate convenience launchers next to vantix.exe so the user (or an ops
REM operator) can double-click client.cmd / server.cmd instead of typing
REM CLI args. The actual binary is vantix.exe; these are tiny wrappers.
REM   - client.cmd  starts the game in client mode (default).
REM                 Uses --verbose and pipes stdout+stderr to vantix.log next to
REM                 the exe so post-mortem debugging (lightmap-load, shader-
REM                 compile errors etc.) is captured without re-running.
REM   - server.cmd  starts headless dedicated server, max 16 players
REM ----------------------------------------------------------------------------
REM client.cmd is kept as a static template (client.cmd.tpl at repo root) and just
REM copied here on build. Avoids cmd's echo-escape AND powershell-from-cmd quoting
REM hell — both got the same `>`/`|`/`&` interpretation wrong in slightly different
REM ways. Edit client.cmd.tpl directly if you want to change launcher behaviour.
copy /Y "%~dp0client.cmd.tpl" "Export\Windows\client.cmd" >nul
echo @"%%~dp0vantix.exe" --headless -- --server --max-players 16>"Export\Windows\server.cmd"

echo.
echo === BUILD OK ===
echo Binary:   %~dp0Export\Windows\vantix.exe
echo Client:   %~dp0Export\Windows\client.cmd          ^(double-click^)
echo Server:   %~dp0Export\Windows\server.cmd          ^(double-click^)
for %%F in ("Export\Windows\vantix.exe") do echo Size:     %%~zF bytes
echo.
endlocal
