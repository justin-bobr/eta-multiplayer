@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0eta.exe' --verbose 2>&1 | Tee-Object -FilePath '%~dp0eta.log'"
@pause
