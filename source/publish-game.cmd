@echo off
setlocal
rem =====================================================================
rem  publish-game.cmd -- one-click build of the Sequence terminal client.
rem
rem  Double-click this after pulling the source. It builds and copies
rem  Sequence.Terminal.exe into source\publish\ next to this script.
rem
rem  Optional first argument:
rem    selfcontained  -> single ~60 MB exe, players need no .NET runtime
rem    (nothing)      -> small exe, players install the .NET 8 Runtime
rem =====================================================================
cd /d "%~dp0"

set "PROJ=src\Sequence.Terminal\Sequence.Terminal.csproj"
set "OUT=publish"
set "ARGS=-c Release -o %OUT%"

if /I "%~1"=="selfcontained" (
    set "ARGS=%ARGS% -r win-x64 --self-contained true -p:PublishSingleFile=true"
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet is not installed or not on PATH.
    echo         Install the .NET 8 SDK from https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo Building Sequence.Terminal%ARGS% ...
call dotnet publish %PROJ% %ARGS% --nologo -v m
if errorlevel 1 (
    echo.
    echo [BUILD FAILED] See the messages above.
    pause
    exit /b 1
)

if not exist "%OUT%\Sequence.Terminal.exe" (
    echo.
    echo [BUILD FAILED] Sequence.Terminal.exe was not produced. See the messages above.
    pause
    exit /b 1
)

for %%F in ("%OUT%\Sequence.Terminal.exe") do set "SIZE=%%~zF"

echo.
echo Build OK.
if /I "%~1"=="selfcontained" (
    echo Copy just these to each player's machine:
) else (
    echo Copy this one file to each player's machine (they need the .NET 8 Runtime):
)
echo   %CD%\%OUT%\Sequence.Terminal.exe
echo Host the game with:  Sequence.Terminal.exe --server --bind 0.0.0.0
echo Join with:           Sequence.Terminal.exe --client --host ^<server-ip^> --player your-id
rem Ensure the window description renders before we pause:
timeout /t 2 >nul
echo.
pause
