@echo off
chcp 65001 >nul
title Instagram OAuth - Python Test

echo.
echo ==========================================
echo   Instagram OAuth Server - Python Test
echo ==========================================
echo.

cd /d "%~dp0"

where python >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Python not found. Please install from https://python.org
    pause
    exit /b 1
)

python -c "import requests" >nul 2>&1
if errorlevel 1 (
    echo [INFO] 'requests' library not found, installing...
    pip install requests
    echo.
)

echo [INFO] Make sure the server is running first: npm start
echo.

python test_server.py --url http://localhost:3000

echo.
pause
