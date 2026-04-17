@echo off
cd /d "%~dp0"
title Railroader Tile Editor

echo ================================================
echo   Railroader Tile Editor - Starting up...
echo ================================================
echo.

:: Check Python is installed
python --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Python is not installed or not in PATH.
    echo.
    echo Please install Python from https://www.python.org/downloads/
    echo Make sure to check "Add Python to PATH" during installation.
    echo.
    pause
    exit /b 1
)

for /f "tokens=*" %%i in ('python --version 2^>^&1') do echo [OK] Found %%i

echo.
echo [....] Checking required libraries...
echo.

set MISSING=0

:: pygame-ce installs as "pygame-ce" but imports as "pygame"
python -c "import pygame" >nul 2>&1
if errorlevel 1 ( echo [ !! ] Missing: pygame-ce  & set MISSING=1 ) else ( echo [ OK ] pygame-ce )

:: numpy
python -c "import numpy" >nul 2>&1
if errorlevel 1 ( echo [ !! ] Missing: numpy  & set MISSING=1 ) else ( echo [ OK ] numpy )

:: Pillow installs as "Pillow" but imports as "PIL"
python -c "import PIL" >nul 2>&1
if errorlevel 1 ( echo [ !! ] Missing: Pillow  & set MISSING=1 ) else ( echo [ OK ] Pillow )

:: requests
python -c "import requests" >nul 2>&1
if errorlevel 1 ( echo [ !! ] Missing: requests  & set MISSING=1 ) else ( echo [ OK ] requests )

:: scipy
python -c "import scipy" >nul 2>&1
if errorlevel 1 ( echo [ !! ] Missing: scipy  & set MISSING=1 ) else ( echo [ OK ] scipy )

if %MISSING%==1 (
    echo.
    echo [....] Installing missing libraries, please wait...
    echo.
    pip install pygame-ce numpy Pillow requests scipy
    if errorlevel 1 (
        echo.
        echo [ERROR] Failed to install one or more libraries.
        echo Try running this file as Administrator, or install manually with:
        echo   pip install pygame-ce numpy Pillow requests scipy
        echo.
        pause
        exit /b 1
    )
    echo.
    echo [OK] All libraries installed successfully.
)

echo.
echo ================================================
echo   Launching editor...
echo ================================================
echo.

python -m edit_tiles %*

if errorlevel 1 (
    echo.
    echo [ERROR] The editor exited with an error.
    echo Check crash.log in this folder for details.
    echo.
    pause
)
