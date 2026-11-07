@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0validate_resource_update_notices.ps1" %*
exit /b %ERRORLEVEL%
