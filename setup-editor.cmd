@echo off
REM Thin wrapper - real logic in setup-editor.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-editor.ps1"
