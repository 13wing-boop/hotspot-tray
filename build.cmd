@echo off
setlocal
rem HotspotTray build - no SDK required, uses the in-box .NET Framework compiler.

set NETFX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WINMD=%WINDIR%\System32\WinMetadata
set OUT=%~dp0bin

if not exist "%NETFX%\csc.exe" (
  echo [!] csc.exe not found: %NETFX%
  exit /b 1
)
if not exist "%WINMD%\Windows.Networking.winmd" (
  echo [!] WinRT metadata not found: %WINMD%
  exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"

"%NETFX%\csc.exe" ^
  /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:"%OUT%\HotspotTray.exe" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:"%NETFX%\System.Runtime.dll" ^
  /reference:"%WINMD%\Windows.Foundation.winmd" ^
  /reference:"%WINMD%\Windows.Networking.winmd" ^
  "%~dp0src\HotspotTray.cs"

if errorlevel 1 (
  echo [!] BUILD FAILED
  exit /b 1
)
echo [OK] %OUT%\HotspotTray.exe
endlocal
