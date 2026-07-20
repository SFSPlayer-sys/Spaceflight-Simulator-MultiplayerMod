@echo off
set PROJECT_DIR=C:\Users\Administrator\Downloads\Other\Multiplayer-SFS-main\Multiplayer-SFS-main
set SERVER_DIR=%PROJECT_DIR%\Server
set MOD_DIR=%PROJECT_DIR%\Mod
set RELEASE_DIR=%PROJECT_DIR%\Release

if not exist "%RELEASE_DIR%" mkdir "%RELEASE_DIR%"

cd /d "%MOD_DIR%"
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 goto :error

if not exist "%RELEASE_DIR%\Mod" mkdir "%RELEASE_DIR%\Mod"
copy "%MOD_DIR%\bin\Release\net4.8\Mod.dll" "%RELEASE_DIR%\Mod\" /Y >nul
copy "%MOD_DIR%\bin\Release\net4.8\Lidgren.Network.dll" "%RELEASE_DIR%\Mod\" /Y >nul

cd /d "%SERVER_DIR%"
dotnet publish -c Release -f net6.0 -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Windows-x64-NonSelfContained"
if %ERRORLEVEL% NEQ 0 goto :error

dotnet publish -c Release -f net6.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Windows-x64-SelfContained"
if %ERRORLEVEL% NEQ 0 goto :error

dotnet publish -c Release -f net6.0 -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Linux-x64-SelfContained"
if %ERRORLEVEL% NEQ 0 goto :error

dotnet publish -c Release -f net6.0 -r linux-arm64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Linux-ARM64-SelfContained"
if %ERRORLEVEL% NEQ 0 goto :error

dotnet publish -c Release -f net6.0 -r linux-arm --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "%RELEASE_DIR%\Server-Linux-ARM-SelfContained"
if %ERRORLEVEL% NEQ 0 goto :error

set SFS_MODS_DIR=C:\Program Files (x86)\Steam\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game\Mods
if not exist "%SFS_MODS_DIR%" mkdir "%SFS_MODS_DIR%"
copy "%RELEASE_DIR%\Mod\Mod.dll" "%SFS_MODS_DIR%\" /Y >nul 2>&1

start "" "%RELEASE_DIR%\Server-Windows-x64-NonSelfContained\Server.exe"

set SFS_EXE=C:\Program Files (x86)\Steam\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game\Spaceflight Simulator.exe
start "" "%SFS_EXE%"
timeout /t 2 >nul
start "" "%SFS_EXE%"

goto :end

:error
echo BUILD FAILED!
exit /b 1

:end
pause
