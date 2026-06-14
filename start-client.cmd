@echo off
REM ============================================================================
REM  VANTIX Client in dev-mode via the Editor runtime (no build needed).
REM  Connects to 127.0.0.1:27015 by default.
REM
REM  Random Name-Suffix per Launch -> jede Instanz wird vom Server als
REM  eigener Spieler erkannt (kein Reconnect-Konflikt bei Multi-Client-Tests).
REM
REM  Aufruf:    start-client.cmd
REM             start-client.cmd 192.168.1.10            (Remote-Server)
REM             start-client.cmd 192.168.1.10:27015      (Custom Port)
REM ============================================================================
setlocal
cd /d "%~dp0"

if defined GODOT if not exist "%GODOT%" set "GODOT="
if not defined GODOT set "GODOT=%~dp0Engine\Editor_console.exe"
if not exist "%GODOT%" (
    echo [WARN] %GODOT% not found - falling back to 'godot' on PATH
    set "GODOT=godot"
)

REM Target: erstes Cmdline-Argument oder Default-Localhost
set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=127.0.0.1:27015"

REM Random Name-Suffix damit jeder Client einen unique Display-Namen kriegt
set "PNAME=Player_%RANDOM%"

echo.
echo === VANTIX CLIENT - dev-mode via Editor ===
echo Godot:    %GODOT%
echo Path:     %~dp0Game
echo Target:   %TARGET%
echo Name:     %PNAME%
echo.
echo Identity-Token wird vom Server assigned + im user://settings.cfg persistiert.
echo.

"%GODOT%" --path "Game" -- --connect %TARGET% --name "%PNAME%"

echo.
echo === Client beendet ===
pause
endlocal
