@echo off
chcp 65001 >nul
title Dang bien dich Sieu Nhan Hong...
pushd "%~dp0"
echo ============================================================
echo   DANG BIEN DICH: SIEU NHAN HONG - VUOT CHAN STEAM v4
echo ============================================================
echo.

set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe

"%CSC%" /nologo ^
    /target:winexe ^
    /platform:x64 ^
    /optimize+ ^
    /codepage:65001 ^
    /out:"SieuNhanHong.exe" ^
    /win32manifest:"app.manifest" ^
    /win32icon:"app.ico" ^
    /resource:logo.png,AppIcon.png ^
    /resource:goodbyedpi\goodbyedpi.exe,gdpi.goodbyedpi.exe ^
    /resource:goodbyedpi\WinDivert.dll,gdpi.WinDivert.dll ^
    /resource:goodbyedpi\WinDivert64.sys,gdpi.WinDivert64.sys ^
    /resource:goodbyedpi\licenses\LICENSE-goodbyedpi.txt,gdpi.LICENSE-goodbyedpi.txt ^
    /resource:goodbyedpi\licenses\LICENSE-windivert.txt,gdpi.LICENSE-windivert.txt ^
    /resource:goodbyedpi\licenses\LICENSE-getline.txt,gdpi.LICENSE-getline.txt ^
    /resource:goodbyedpi\licenses\LICENSE-uthash.txt,gdpi.LICENSE-uthash.txt ^
    /r:System.dll ^
    /r:System.Core.dll ^
    /r:System.Drawing.dll ^
    /r:System.ServiceProcess.dll ^
    /r:"%FW%\System.Web.Extensions.dll" ^
    /r:"%FW%\System.IO.Compression.dll" ^
    /r:"%FW%\System.IO.Compression.FileSystem.dll" ^
    /r:"%FW%\System.Xaml.dll" ^
    /r:"%FW%\WPF\WindowsBase.dll" ^
    /r:"%FW%\WPF\PresentationCore.dll" ^
    /r:"%FW%\WPF\PresentationFramework.dll" ^
    "SieuNhanHong.cs"

set RESULT=%errorlevel%
if %RESULT% equ 0 (
    echo.
    echo   [THANH CONG] Da tao file: SieuNhanHong.exe
) else (
    echo.
    echo   [LOI] Bien dich that bai! Ma loi: %RESULT%
)
popd
exit /b %RESULT%
