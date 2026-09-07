@echo off
setlocal
set APP=%~dp0bin\Release\net8.0-windows\win-x64\publish\MYKEY.exe

if not exist "%APP%" (
    call "%~dp0publish-desktop.bat"
)

start "" "%APP%"
