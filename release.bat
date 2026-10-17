@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\make-release.ps1"
if errorlevel 1 (echo. & echo ОШИБКА: релиз не создан. & pause & exit /b 1)
pause
