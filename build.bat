@echo off
echo ========================================
echo Multiplayer-SFS Build Script
echo ========================================
echo.

set PROJECT_DIR=C:\Users\Administrator\Downloads\Other\Multiplayer-SFS-main\Multiplayer-SFS-main
set SERVER_DIR=%PROJECT_DIR%\Server
set MOD_DIR=%PROJECT_DIR%\Mod
set RELEASE_DIR=%PROJECT_DIR%\Release

echo Creating release directory structure...
if not exist "%RELEASE_DIR%" mkdir "%RELEASE_DIR%"

echo.
echo ========================================
echo Building Mod
echo ========================================
cd /d "%MOD_DIR%"
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Mod build failed!
    goto :error
)

echo.
echo ========================================
echo Copying Mod files
echo ========================================
if not exist "%RELEASE_DIR%\Mod" mkdir "%RELEASE_DIR%\Mod"
copy "%MOD_DIR%\bin\Release\net4.8\Mod.dll" "%RELEASE_DIR%\Mod\" /Y
copy "%MOD_DIR%\bin\Release\net4.8\Lidgren.Network.dll" "%RELEASE_DIR%\Mod\" /Y

echo.
echo ========================================
echo Building Server for Windows x64 - Non-Self-Contained
echo ========================================
cd /d "%SERVER_DIR%"
dotnet publish -c Release -f net6.0 -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Windows-x64-NonSelfContained"
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Server Windows x64 non-self-contained build failed!
    goto :error
)

echo.
echo ========================================
echo Building Server for Windows x64 - Self-Contained
echo ========================================
dotnet publish -c Release -f net6.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Windows-x64-SelfContained"
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Server Windows x64 self-contained build failed!
    goto :error
)

echo.
echo ========================================
echo Building Server for Linux x64 - Self-Contained
echo ========================================
cd /d "%SERVER_DIR%"
dotnet publish -c Release -f net6.0 -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Linux-x64-SelfContained"
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Server Linux x64 self-contained build failed!
    goto :error
)

echo.
echo ========================================
echo Building Server for Linux ARM64 - Self-Contained
echo ========================================
dotnet publish -c Release -f net6.0 -r linux-arm64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Linux-ARM64-SelfContained"
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Server Linux ARM64 self-contained build failed!
    goto :error
)

echo.
echo ========================================
echo Building Server for Linux ARM - Self-Contained
echo ========================================
dotnet publish -c Release -f net6.0 -r linux-arm --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Linux-ARM-SelfContained"
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Server Linux ARM self-contained build failed!
    goto :error
)

echo.
echo ========================================
echo Build completed successfully!
echo ========================================
echo.
echo Release directory: %RELEASE_DIR%
echo.
echo Contents:
echo - Mod\Mod.dll (Mod for SFS)
echo - Mod\Lidgren.Network.dll
echo - Server-Windows-x64-NonSelfContained\ (Requires .NET 6.0 runtime, single-file output)
echo - Server-Windows-x64-SelfContained\ (Standalone, no .NET required, single-file output)
echo - Server-Linux-x64-SelfContained\ (Standalone Linux x64, single-file output)
echo - Server-Linux-ARM64-SelfContained\ (Standalone Linux ARM64, single-file output)
echo - Server-Linux-ARM-SelfContained\ (Standalone Linux ARM, single-file output)
echo.

echo ========================================
echo Copying Mod.dll to SFS Mods folder
echo ========================================
set SFS_MODS_DIR=C:\Program Files (x86)\Steam\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game\Mods
if not exist "%SFS_MODS_DIR%" mkdir "%SFS_MODS_DIR%"
copy "%RELEASE_DIR%\Mod\Mod.dll" "%SFS_MODS_DIR%\" /Y
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: Failed to copy Mod.dll to SFS Mods folder!
) else (
    echo Mod.dll copied successfully!
)

echo.
echo ========================================
echo Starting Server
echo ========================================
start "" "%RELEASE_DIR%\Server-Windows-x64-NonSelfContained\Server.exe"

echo.
echo ========================================
echo Starting SFS Instances
echo ========================================
set SFS_EXE=C:\Program Files (x86)\Steam\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game\Spaceflight Simulator.exe
start "" "%SFS_EXE%"
timeout /t 2 >nul
start "" "%SFS_EXE%"

echo.
echo ========================================
echo All tasks completed!
echo ========================================
echo.
goto :end

:error
echo.
echo ========================================
echo BUILD FAILED!
echo ========================================
exit /b 1

:end
pause
