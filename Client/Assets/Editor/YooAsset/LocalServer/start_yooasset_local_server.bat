@echo off
setlocal
set "PORT=%~1"
set "ROOT=%~2"
title OutpostLoop YooAsset Resource Server - %PORT%
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_yooasset_local_server.ps1" -Port %PORT% -Root "%ROOT%"
exit /b %errorlevel%
