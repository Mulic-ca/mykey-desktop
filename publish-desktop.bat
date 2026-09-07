@echo off
setlocal
set DOTNET=%~dp0.dotnet\dotnet.exe
set PUBLISH_DIR=%~dp0bin\Release\net8.0-windows\win-x64\publish

if not exist "%DOTNET%" (
    where dotnet >nul 2>nul
    if errorlevel 1 (
        echo .NET 8 SDK not found.
        exit /b 1
    )
    set DOTNET=dotnet
)

if exist "%PUBLISH_DIR%" (
    del /q "%PUBLISH_DIR%\MYKEY.exe" 2>nul
    del /q "%PUBLISH_DIR%\MYKEY.pdb" 2>nul
)

"%DOTNET%" publish "%~dp0MyKey.Desktop.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:EnableCompressionInSingleFile=true
if errorlevel 1 exit /b 1

echo.
echo Published to:
echo %PUBLISH_DIR%
