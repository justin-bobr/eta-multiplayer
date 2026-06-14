@echo off
REM Thin wrapper - real logic in list-modules.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0list-modules.ps1"
