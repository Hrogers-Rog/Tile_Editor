@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package_complete_mod.ps1" %*
if errorlevel 1 (
    echo.
    echo [ERROR] Package build failed.
    pause
    exit /b 1
)
echo.
echo [OK] Complete package build finished.
endlocal
