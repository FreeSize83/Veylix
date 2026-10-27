@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" goto nocsc

set "REFS=/lib:"%FW%\WPF" /r:PresentationCore.dll /r:PresentationFramework.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll"

if not exist dist mkdir dist
echo Сборка Veylix.exe ...
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:dist\Veylix.exe /win32icon:Veylix.ico /win32manifest:Veylix.manifest /resource:zapret.zip,zapret.zip /resource:presets\apps.json,apps.json %REFS% src\*.cs
if errorlevel 1 goto fail

if /i "%~1"=="test" (
  echo Сборка и запуск тестов ...
  "%FW%\csc.exe" /nologo /codepage:65001 /target:exe /out:dist\VeylixTests.exe /main:Veylix.Tests.TestProgram /win32manifest:tests\tests.manifest /resource:zapret.zip,zapret.zip /resource:presets\apps.json,apps.json %REFS% src\*.cs tests\*.cs
  if errorlevel 1 goto fail
  dist\VeylixTests.exe
  if errorlevel 1 goto fail
)

echo.
echo ГОТОВО: %~dp0dist\Veylix.exe
echo Запускайте двойным кликом (запросит права администратора).
if /i not "%~1"=="test" pause
exit /b 0

:nocsc
echo Не найден csc.exe из .NET Framework 4.x. Он есть в Windows 10/11 по умолчанию.
pause
exit /b 1

:fail
echo.
echo ОШИБКА СБОРКИ ИЛИ ТЕСТОВ. Скопируйте текст выше.
if /i not "%~1"=="test" pause
exit /b 1
