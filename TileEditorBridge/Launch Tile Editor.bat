@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
title Hrogers Tile Editor Suite 0.16.4

set "EDITOR_DIR=%~dp0TileEditor"
set "VENV_DIR=%EDITOR_DIR%\.venv"
set "VENV_PYTHON=%VENV_DIR%\Scripts\python.exe"
set "PYTHON_FINDER=%~dp0Find Tile Editor Python.ps1"
for %%I in ("%~dp0..\..") do set "INSTALLED_GAME_DIR=%%~fI"
if exist "%INSTALLED_GAME_DIR%\Railroader_Data" if exist "%INSTALLED_GAME_DIR%\Mods" (
    set "TILE_EDITOR_GAME_DIR=%INSTALLED_GAME_DIR%"
)

echo ============================================================
echo   Hrogers Tile Editor Suite 0.16.4
echo ============================================================
echo.

if not exist "%PYTHON_FINDER%" (
    echo [ERROR] The Python discovery helper is missing.
    echo Expected: "%PYTHON_FINDER%"
    echo Reinstall the complete Tile Editor package.
    echo.
    pause
    exit /b 1
)

if /i "%~1"=="--diagnose-python" (
    call :find_python
    if errorlevel 1 (
        endlocal
        exit /b 1
    )
    echo [OK] Tile Editor will use:
    echo      "!BASE_PYTHON!"
    endlocal
    exit /b 0
)

if not exist "%EDITOR_DIR%\edit_tiles\__main__.py" (
    echo [ERROR] The packaged TileEditor folder is incomplete.
    echo Expected: "%EDITOR_DIR%\edit_tiles\__main__.py"
    echo.
    pause
    exit /b 1
)

if exist "%VENV_PYTHON%" (
    "%VENV_PYTHON%" -c "import sys,venv; assert sys.version_info >= (3,10)" >nul 2>&1
    if errorlevel 1 (
        echo [REPAIR] The existing isolated Python environment is broken.
        echo          Rebuilding the disposable .venv folder...
        rmdir /s /q "%VENV_DIR%"
    )
)

if not exist "%VENV_PYTHON%" (
    call :find_python
    if errorlevel 1 (
        pause
        exit /b 1
    )
    echo [SETUP] Creating an isolated environment with:
    echo         "!BASE_PYTHON!"
    "!BASE_PYTHON!" -m venv "%VENV_DIR%"
    if errorlevel 1 (
        echo.
        echo [ERROR] Python was found, but it could not create:
        echo         "%VENV_DIR%"
        echo Try "Repair Tile Editor Environment.bat".
        echo.
        pause
        exit /b 1
    )
)

"%VENV_PYTHON%" -c "import pygame,numpy,PIL,requests,scipy" >nul 2>&1
if errorlevel 1 (
    echo [SETUP] Installing Tile Editor dependencies...
    "%VENV_PYTHON%" -m ensurepip --upgrade >nul 2>&1
    "%VENV_PYTHON%" -m pip install --disable-pip-version-check --upgrade pip setuptools wheel
    "%VENV_PYTHON%" -m pip install --disable-pip-version-check --prefer-binary -r "%EDITOR_DIR%\requirements.txt"
    if errorlevel 1 goto :dependency_failed
    "%VENV_PYTHON%" -c "import pygame,numpy,PIL,requests,scipy" >nul 2>&1
    if errorlevel 1 goto :dependency_failed
)

echo [START] Launching the desktop Tile Editor...
echo.
cd /d "%EDITOR_DIR%"
"%VENV_PYTHON%" -m edit_tiles %*
set "EDITOR_EXIT=%ERRORLEVEL%"

if not "%EDITOR_EXIT%"=="0" (
    echo.
    echo [ERROR] The Tile Editor exited with an error.
    echo Review "%EDITOR_DIR%\crash.log" for details.
    echo.
    pause
)

endlocal & exit /b %EDITOR_EXIT%

:find_python
set "BASE_PYTHON="
for /f "usebackq delims=" %%P in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PYTHON_FINDER%" 2^>nul`) do (
    if not defined BASE_PYTHON set "BASE_PYTHON=%%P"
)
if defined BASE_PYTHON (
    for /f "tokens=*" %%V in ('"%BASE_PYTHON%" --version 2^>^&1') do echo [FOUND] %%V
    exit /b 0
)

echo [ERROR] A compatible 64-bit Python 3.10 or newer was not found.
echo.
echo The launcher checked:
echo   - TILE_EDITOR_PYTHON override
echo   - the Windows py launcher
echo   - python/python3 on PATH
echo   - Python registry entries
echo   - python.org, Conda, Scoop, and common install folders
echo.
echo Install 64-bit Python from https://www.python.org/downloads/windows/
echo The "Add Python to PATH" option is helpful but no longer required.
echo.
echo Advanced: set TILE_EDITOR_PYTHON to the full python.exe path.
echo Example:
echo   set TILE_EDITOR_PYTHON=C:\Users\YourName\AppData\Local\Programs\Python\Python313\python.exe
echo.
exit /b 1

:dependency_failed
echo.
echo [ERROR] Dependency installation failed.
echo Verify internet access, then run:
echo   "Repair Tile Editor Environment.bat"
echo.
pause
exit /b 1
