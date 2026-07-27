@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Railroader Tile Editor

set "PYTHON_FINDER=%~dp0TileEditorBridge\Find Tile Editor Python.ps1"
set "BASE_PYTHON="

echo ================================================
echo   Railroader Tile Editor - Starting up...
echo ================================================
echo.

if not exist "%PYTHON_FINDER%" (
    echo [ERROR] Python discovery helper is missing:
    echo         "%PYTHON_FINDER%"
    pause
    exit /b 1
)

for /f "usebackq delims=" %%P in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PYTHON_FINDER%" 2^>nul`) do (
    if not defined BASE_PYTHON set "BASE_PYTHON=%%P"
)
if not defined BASE_PYTHON (
    echo [ERROR] No compatible 64-bit Python 3.10 or newer was found.
    echo Install Python from https://www.python.org/downloads/windows/
    echo or set TILE_EDITOR_PYTHON to the full python.exe path.
    echo.
    pause
    exit /b 1
)

for /f "tokens=*" %%V in ('"%BASE_PYTHON%" --version 2^>^&1') do echo [FOUND] %%V
echo.
echo [....] Checking required libraries...

"%BASE_PYTHON%" -c "import pygame,numpy,PIL,requests,scipy" >nul 2>&1
if errorlevel 1 (
    echo [SETUP] Installing missing libraries...
    "%BASE_PYTHON%" -m ensurepip --upgrade >nul 2>&1
    "%BASE_PYTHON%" -m pip install --disable-pip-version-check --prefer-binary -r "%~dp0requirements.txt"
    if errorlevel 1 (
        echo.
        echo [ERROR] Failed to install required libraries.
        echo.
        pause
        exit /b 1
    )
)

echo.
echo ================================================
echo   Launching editor...
echo ================================================
echo.

"%BASE_PYTHON%" -m edit_tiles %*
set "EDITOR_EXIT=%ERRORLEVEL%"

if not "%EDITOR_EXIT%"=="0" (
    echo.
    echo [ERROR] The editor exited with an error.
    echo Check crash.log in this folder for details.
    echo.
    pause
)

endlocal & exit /b %EDITOR_EXIT%
