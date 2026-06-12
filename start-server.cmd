@echo off
REM ============================================================================
REM  ETA Dedicated Server in dev-mode via the Editor runtime (no build needed).
REM  Server listens on UDP :27015.
REM
REM  For a release binary build instead, run build-server.cmd and execute
REM  Export\Server\server.exe directly.
REM
REM  Optional flags appended to --server:
REM    --max-players 16     server cap, default 16
REM    --gamemode dm        deathmatch instead of competitive
REM    --tickrate 128       server sim rate
REM ============================================================================
setlocal
cd /d "%~dp0"

if defined GODOT if not exist "%GODOT%" set "GODOT="
if not defined GODOT set "GODOT=%~dp0Engine\Editor_console.exe"
if not exist "%GODOT%" (
    echo [WARN] %GODOT% not found - falling back to 'godot' on PATH
    set "GODOT=godot"
)

echo.
echo === ETA SERVER - dev-mode via Editor ===
echo Godot:  %GODOT%
echo Path:   %~dp0Game
echo Port:   27015 UDP
echo.

"%GODOT%" --headless --debug-server tcp://127.0.0.1:7007 --path "Game" -- --server --max-players 16 --gamemode dm

echo.
echo === Server stopped ===
pause
endlocal
