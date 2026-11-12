@echo off
REM ============================================================
REM  ShakeLens (WPF) build script
REM
REM  Needs the .NET 8 SDK with the Windows Desktop workload.
REM  The published app is self contained, so the target machine does
REM  not need the .NET runtime.
REM ============================================================
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [build] the .NET 8 SDK was not found
  exit /b 1
)

if not exist dist mkdir dist
if not exist build mkdir build

echo [build] restoring ...
dotnet restore -v q --nologo
if errorlevel 1 ( echo [build] FAILED: restore & exit /b 1 )

echo [build] publishing ...
dotnet publish -c Release -r win-x64 --self-contained true --nologo -v q -o publish
if errorlevel 1 ( echo [build] FAILED: publish & exit /b 1 )

echo [build] packing ...
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-payload.ps1 -Source publish -Out build\payload.bin
if errorlevel 1 ( echo [build] FAILED: packing & exit /b 1 )

echo [build] building the installer ...
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
"%CSC%" /nologo /target:winexe /optimize+ /platform:x64 /win32icon:assets\ShakeLens.ico ^
   /out:dist\ShakeLens-Setup-x64.exe ^
   /reference:"%FX%\System.Drawing.dll" /reference:"%FX%\System.Windows.Forms.dll" ^
   /reference:"%FX%\System.IO.Compression.dll" ^
   /resource:build\payload.bin,ShakeLens.Payload ^
   tools\setup\ShakeLensSetup.cs
if errorlevel 1 ( echo [build] FAILED: installer & exit /b 1 )

echo [build] zipping the portable build ...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Add-Type -AssemblyName System.IO.Compression.FileSystem; if (Test-Path 'dist\ShakeLens-portable-x64.zip') { Remove-Item 'dist\ShakeLens-portable-x64.zip' -Force }; [System.IO.Compression.ZipFile]::CreateFromDirectory('publish', 'dist\ShakeLens-portable-x64.zip', [System.IO.Compression.CompressionLevel]::Optimal, $false)"
if errorlevel 1 ( echo [build] FAILED: zip & exit /b 1 )

echo.
echo [build] done
echo          dist\ShakeLens-Setup-x64.exe     the installer
echo          dist\ShakeLens-portable-x64.zip the portable build
endlocal
