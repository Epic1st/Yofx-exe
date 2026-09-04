@echo off
echo Starting YO4X Admin Portal on port 5184...
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\Start-AdminPortal.ps1"
pause
