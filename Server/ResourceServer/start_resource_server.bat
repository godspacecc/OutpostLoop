@echo off
setlocal
cd /d "%~dp0"
title OutpostLoop Resource Server
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0resource_server.ps1" -Port 8000 -Root "%~dp0ResourceFiles" -NoticeFile "%~dp0Notices\resource-update-notices.json" -AppUpdateRoot "%~dp0AppUpdates\Android"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo.
    echo Resource server failed. Exit code: %EXIT_CODE%
    pause
)
exit /b %EXIT_CODE%
