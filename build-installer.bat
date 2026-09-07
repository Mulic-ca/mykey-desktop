@echo off
setlocal

call "%~dp0publish-desktop.bat"
if errorlevel 1 exit /b 1

set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe

if not exist "%ISCC%" (
    echo Inno Setup compiler not found.
    exit /b 1
)

"%ISCC%" "%~dp0installer\MYKEY.iss"
if errorlevel 1 exit /b 1

echo.
echo Installer created:
echo %~dp0dist\MYKEY-Setup-1.1.0-x64.exe
