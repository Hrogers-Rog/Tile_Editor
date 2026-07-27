@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
title Repair Hrogers Tile Editor Environment

set "EDITOR_DIR=%~dp0TileEditor"
set "VENV_DIR=%EDITOR_DIR%\.venv"
set "VENV_PYTHON=%VENV_DIR%\Scripts\python.exe"
set "PYTHON_FINDER=%~dp0Find Tile Editor Python.ps1"

if not exist "%EDITOR_DIR%\requirements.txt" (
    echo [ERROR] The packaged TileEditor folder is incomplete.
    pause
    exit /b 1
)

if not exist "%PYTHON_FINDER%" (
    echo [ERROR] The Python discovery helper is missing.
    echo Reinstall the complete Tile Editor package.
    pause
    exit /b 1
)

set "REBUILD_VENV=0"
if not exist "%VENV_PYTHON%" set "REBUILD_VENV=1"
if exist "%VENV_PYTHON%" (
    "%VENV_PYTHON%" -c "import sys,venv; assert sys.version_info >= (3,10)" >nul 2>&1
    if errorlevel 1 set "REBUILD_VENV=1"
)

if "%REBUILD_VENV%"=="1" (
    if exist "%VENV_DIR%" (
        echo Removing the broken isolated environment...
        rmdir /s /q "%VENV_DIR%"
    )
    call :find_python
    if errorlevel 1 goto :failed
    echo Creating a fresh isolated environment with:
    echo   "!BASE_PYTHON!"
    "!BASE_PYTHON!" -m venv "%VENV_DIR%"
    if errorlevel 1 goto :failed
)

echo Updating packaging tools...
"%VENV_PYTHON%" -m ensurepip --upgrade
if errorlevel 1 goto :failed
"%VENV_PYTHON%" -m pip install --disable-pip-version-check --upgrade pip setuptools wheel
if errorlevel 1 goto :failed

echo Reinstalling Tile Editor requirements...
"%VENV_PYTHON%" -m pip install --disable-pip-version-check --prefer-binary --upgrade --force-reinstall -r "%EDITOR_DIR%\requirements.txt"
if errorlevel 1 goto :failed

echo Verifying imports...
"%VENV_PYTHON%" -c "import pygame,numpy,PIL,requests,scipy"
if errorlevel 1 goto :failed

echo.
echo [OK] The Tile Editor environment is ready.
pause
exit /b 0

:find_python
set "BASE_PYTHON="
for /f "usebackq delims=" %%P in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PYTHON_FINDER%" 2^>nul`) do (
    if not defined BASE_PYTHON set "BASE_PYTHON=%%P"
)
if defined BASE_PYTHON (
    for /f "tokens=*" %%V in ('"%BASE_PYTHON%" --version 2^>^&1') do echo [FOUND] %%V
    exit /b 0
)
echo [ERROR] No compatible 64-bit Python 3.10 or newer was found.
echo Install it from https://www.python.org/downloads/windows/
echo or set TILE_EDITOR_PYTHON to the full python.exe path.
exit /b 1

:failed
echo.
echo [ERROR] Repair failed. Review the messages above.
pause
exit /b 1
