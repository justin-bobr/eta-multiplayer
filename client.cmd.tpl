@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0vantix.exe' --verbose 2>&1 | Tee-Object -FilePath '%~dp0vantix.log'"
@pause
