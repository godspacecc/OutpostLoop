@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate.ps1"
exit /b %errorlevel%
