@echo off
REM ============================================================================
REM  VANTIX Windows - DEBUG Build.
REM  Uses Godot's debug export template (stock from setup-editor.cmd, NOT the
REM  custom stripped one). Includes verbose error reporting + .NET debug symbols
REM  so runtime errors print full stack traces.
REM
REM  Output: D:\Godot\Export\Windows-Debug\vantix.exe
REM
REM  Run:
REM    Export\Windows-Debug\vantix.exe                                -> client w/ debug logs
REM    Export\Windows-Debug\vantix.exe --headless -- --server         -> dedicated server debug
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
echo === VANTIX WINDOWS DEBUG BUILD ===
echo Godot:    %GODOT%
echo Project:  %~dp0Game
echo Output:   %~dp0Export\Windows-Debug\vantix.exe
echo.

echo [1/2] dotnet build, ExportDebug...
dotnet build "Game\vantix.csproj" -c ExportDebug -nologo
if errorlevel 1 (
    echo [ERR] dotnet build failed
    exit /b 1
)

echo.
echo [2/2] Godot export-debug...
if not exist "Export\Windows-Debug" mkdir "Export\Windows-Debug"
"%GODOT%" --headless --path "Game" --export-debug "Windows" "%~dp0Export\Windows-Debug\vantix.exe"
if errorlevel 1 (
    echo [ERR] Godot export failed
    exit /b 1
)

echo.
echo === BUILD OK ===
echo Run:   %~dp0Export\Windows-Debug\vantix.exe
echo        vantix.exe in cmd window for full stdout - debug-template prints all errors and stack traces
for %%F in ("Export\Windows-Debug\vantix.exe") do echo Size:  %%~zF bytes
echo.
endlocal
